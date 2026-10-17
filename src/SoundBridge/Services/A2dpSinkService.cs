using Windows.Media.Audio;
using Windows.Devices.Enumeration;

namespace SoundBridge.Services;

public enum ConnectResult
{
    Success,
    AlreadyConnected,
    NotSupported,
    TimedOut,
    DeniedBySystem,
    Failed,
}

public sealed record AudioDeviceInfo(string Id, string Name);

public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStateChangedEventArgs(string deviceId, bool isConnected) =>
        (DeviceId, IsConnected) = (deviceId, isConnected);

    public string DeviceId { get; }
    public bool IsConnected { get; }
}

/// <summary>
/// Manages Bluetooth A2DP Sink ("audio playback") connections.
///
/// This is the direct C# / WinRT equivalent of the connection engine in
/// ysc3839's AudioPlaybackConnector (MIT): it uses the
/// Windows.Media.Audio.AudioPlaybackConnection API introduced in
/// Windows 10 2004 to let this PC act as an A2DP sink (a "Bluetooth speaker")
/// for a phone or other paired audio source.
///
/// Naming note: despite being a Bluetooth feature, the API lives in the
/// Windows.Media.Audio namespace (the original C++ app includes
/// winrt/Windows.Media.Audio.h) - there is no Windows.Devices.Bluetooth.Audio.
///
/// Connection flow (identical to the original):
///   1. AudioPlaybackConnection.TryCreateFromId(deviceInformationId)
///   2. await connection.StartAsync()   - tell the system we want audio
///   3. await connection.OpenAsync()    - establish the A2DP link
///   4. observe StateChanged for lifecycle / drops
/// </summary>
public sealed class A2dpSinkService
{
    // deviceId (DeviceInformation.Id) -> live connection. Guarded by _gate.
    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();
    private readonly object _gate = new();

    // Friendly-name cache so StateChanged callbacks (which only carry an id)
    // can still produce human-readable notifications.
    private readonly Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised whenever a connection opens or closes (from a background thread).</summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Enumerates paired devices that support A2DP sink connection,
    /// i.e. exactly the set the original app's DevicePicker showed.
    /// </summary>
    public async Task<IReadOnlyList<AudioDeviceInfo>> GetConnectableDevicesAsync()
    {
        var selector = AudioPlaybackConnection.GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector);

        var result = new List<AudioDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            string name;
            try
            {
                name = string.IsNullOrWhiteSpace(device.Name) ? "Unknown device" : device.Name;
            }
            catch (Exception)
            {
                name = "Unknown device";
            }

            _nameCache[device.Id] = name;
            result.Add(new AudioDeviceInfo(device.Id, name));
        }

        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? GetDeviceName(string deviceId) =>
        _nameCache.TryGetValue(deviceId, out var name) ? name : null;

    public bool IsConnected(string deviceId)
    {
        lock (_gate)
        {
            return _connections.ContainsKey(deviceId);
        }
    }

    public IReadOnlyList<string> ConnectedDeviceIds
    {
        get
        {
            lock (_gate)
            {
                return _connections.Keys.ToList();
            }
        }
    }

    /// <summary>Establishes an audio playback connection with the given device.</summary>
    public async Task<ConnectResult> ConnectAsync(AudioDeviceInfo device)
    {
        if (IsConnected(device.Id))
        {
            return ConnectResult.AlreadyConnected;
        }

        _nameCache[device.Id] = device.Name;

        var connection = AudioPlaybackConnection.TryCreateFromId(device.Id);
        if (connection is null)
        {
            // The device is paired but not currently available (out of range,
            // powered off) or does not expose the A2DP source service.
            return ConnectResult.NotSupported;
        }

        connection.StateChanged += OnConnectionStateChanged;

        lock (_gate)
        {
            _connections[device.Id] = connection;
        }

        try
        {
            await connection.StartAsync();
            var result = await connection.OpenAsync();

            return result.Status switch
            {
                AudioPlaybackConnectionOpenResultStatus.Success => ConnectResult.Success,
                AudioPlaybackConnectionOpenResultStatus.RequestTimedOut => TimedOutCleanup(device.Id),
                AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => DeniedCleanup(device.Id),
                _ => FailedCleanup(device.Id),
            };
        }
        catch (Exception)
        {
            return FailedCleanup(device.Id);
        }
    }

    /// <summary>Closes and removes an active connection (if any).</summary>
    public void Disconnect(string deviceId)
    {
        AudioPlaybackConnection? connection;
        lock (_gate)
        {
            if (!_connections.Remove(deviceId, out connection))
            {
                return;
            }
        }

        connection.StateChanged -= OnConnectionStateChanged;
        try
        {
            // WinRT IClosable.Close() is projected as IDisposable.Dispose() in C#
            // (the C++ original calls Close() directly). Closing an already-closed
            // connection is a no-op, so the catch is purely defensive.
            connection.Dispose();
        }
        catch (Exception)
        {
            // Already closed by the system - nothing to do.
        }

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(deviceId, false));
    }

    /// <summary>Tears down every live connection (used on app exit).</summary>
    public void DisconnectAll()
    {
        List<string> ids;
        lock (_gate)
        {
            ids = _connections.Keys.ToList();
        }

        foreach (var id in ids)
        {
            Disconnect(id);
        }
    }

    private ConnectResult TimedOutCleanup(string deviceId)
    {
        RemoveAndDispose(deviceId);
        return ConnectResult.TimedOut;
    }

    private ConnectResult DeniedCleanup(string deviceId)
    {
        RemoveAndDispose(deviceId);
        return ConnectResult.DeniedBySystem;
    }

    private ConnectResult FailedCleanup(string deviceId)
    {
        RemoveAndDispose(deviceId);
        return ConnectResult.Failed;
    }

    private void RemoveAndDispose(string deviceId)
    {
        AudioPlaybackConnection? connection;
        lock (_gate)
        {
            if (!_connections.Remove(deviceId, out connection))
            {
                return;
            }
        }

        connection.StateChanged -= OnConnectionStateChanged;
        try
        {
            // See Disconnect(): IClosable.Close() == IDisposable.Dispose() in C#.
            connection.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        var id = sender.DeviceId;
        var isOpened = sender.State == AudioPlaybackConnectionState.Opened;

        if (!isOpened)
        {
            // The remote device dropped the link. Clean up exactly like the
            // original app does; the reconnect watchdog will pick favorites
            // back up on its next tick.
            lock (_gate)
            {
                if (_connections.Remove(id, out var stale))
                {
                    stale.StateChanged -= OnConnectionStateChanged;
                    stale.Dispose();
                }
            }
        }

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(id, isOpened));
    }
}
