using SoundBridge.Models;

namespace SoundBridge.Services;

/// <summary>
/// Connection watchdog: re-establishes the A2DP audio link for favorite
/// devices whenever it drops (walked out of range, auto-off, ...).
///
/// Addresses the most common complaint on the original project's tracker:
/// "need to reconnect repeatedly when the phone comes back" (#issues 6, 9, 24).
///
/// Two complementary mechanisms:
///   1. EVENT-DRIVEN BURST — a StateChanged(Closed) drop of a favorite device
///      immediately schedules retries (+5s, +15s, +30s). This catches the
///      "walked out of range and came back a minute later" case without any
///      polling delay.
///   2. POLL SAFETY NET — every ReconnectIntervalSeconds (with adaptive
///      backoff up to 10x) all favorites are checked. Catches everything the
///      event path can miss (device powered on while app already running,
///      radio resets, missed events).
///
/// Manual disconnects are honored: MainWindow calls <see cref="Suppress"/>
/// so the watchdog will not fight the user until the device connects again.
/// </summary>
public sealed class ReconnectService
{
    private readonly A2dpSinkService _bluetooth;
    private readonly SettingsService _settings;
    private readonly Func<TrayService?> _trayProvider;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Devices the user disconnected manually — never auto-reconnect these
    // until they connect again (by hand, by event burst, or by poll).
    private readonly HashSet<string> _suppressed = new(StringComparer.OrdinalIgnoreCase);

    // One cancelable burst per device id, so repeated drops replace the
    // pending burst instead of stacking retries.
    private readonly Dictionary<string, CancellationTokenSource> _bursts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Retry delays (seconds) for the event-driven burst.</summary>
    private static readonly int[] BurstDelaysSeconds = [5, 15, 30];

    public ReconnectService(A2dpSinkService bluetooth, SettingsService settings, Func<TrayService?> trayProvider)
    {
        _bluetooth = bluetooth;
        _settings = settings;
        _trayProvider = trayProvider;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Loop was awaiting a connect attempt; cancellation is best effort on shutdown.
        }

        List<CancellationTokenSource> bursts;
        lock (_gate)
        {
            bursts = _bursts.Values.ToList();
            _bursts.Clear();
        }

        foreach (var burst in bursts)
        {
            burst.Cancel();
        }
    }

    // ----- Event-driven hooks (wired up by App) ------------------------------

    /// <summary>
    /// A device opened an audio connection. Clears any manual-disconnect
    /// suppression and cancels pending bursts for it.
    /// </summary>
    public void OnConnected(string deviceId)
    {
        CancelBurst(deviceId);
        lock (_gate)
        {
            _suppressed.Remove(deviceId);
        }
    }

    /// <summary>
    /// The user disconnected this device on purpose; do not auto-reconnect
    /// it until the next successful connection.
    /// </summary>
    public void Suppress(string deviceId)
    {
        CancelBurst(deviceId);
        lock (_gate)
        {
            _suppressed.Add(deviceId);
        }
    }

    /// <summary>
    /// A favorite device dropped its audio connection (or was reported
    /// closed). Schedules an immediate event-driven retry burst. Called for
    /// every closed connection — cheap no-op for non-favorites.
    /// </summary>
    public void OnConnectionDropped(string deviceId)
    {
        if (!_settings.Current.AutoReconnect)
        {
            return;
        }

        lock (_gate)
        {
            if (_suppressed.Contains(deviceId) ||
                !_settings.Current.FavoriteDeviceIds.Contains(deviceId) ||
                _bursts.ContainsKey(deviceId))
            {
                return;
            }
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _bursts[deviceId] = cts;
        }

        _ = Task.Run(() => BurstAsync(deviceId, cts));
    }

    /// <summary>Event-driven retry burst: +5s, +15s, +30s after a drop.</summary>
    private async Task BurstAsync(string deviceId, CancellationTokenSource cts)
    {
        try
        {
            var name = _bluetooth.GetDeviceName(deviceId) ?? "your device";

            foreach (var delay in BurstDelaysSeconds)
            {
                if (!DelayAsync(TimeSpan.FromSeconds(delay), cts.Token))
                {
                    return; // canceled, suppressed, or already reconnected
                }

                if (_bluetooth.IsConnected(deviceId))
                {
                    return;
                }

                var result = await _bluetooth.ConnectAsync(new AudioDeviceInfo(deviceId, name));
                if (result == ConnectResult.Success)
                {
                    // OnConnected (raised by App) already cleared bookkeeping;
                    // just tell the user the link is back.
                    Notify("Audio reconnected", $"{name} is streaming to this PC again.");
                    return;
                }
            }
        }
        catch (Exception)
        {
            // The poll safety net covers anything that goes wrong here.
        }
        finally
        {
            cts.Dispose();
            lock (_gate)
            {
                if (_bursts.TryGetValue(deviceId, out var current) && current == cts)
                {
                    _bursts.Remove(deviceId);
                }
            }
        }
    }

    private void CancelBurst(string deviceId)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_bursts.Remove(deviceId, out cts))
            {
                return;
            }
        }

        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // First pass after a short grace period so startup isn't slowed down
        // and Windows has time to bring the Bluetooth radio up.
        if (!DelayAsync(TimeSpan.FromSeconds(10), ct))
        {
            return;
        }

        double backoff = 1.0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = _settings.Current;
                if (settings.AutoReconnect)
                {
                    var anySuccess = await TryReconnectFavoritesAsync(ct);

                    // Adaptive interval: reset on success, stretch on failure.
                    backoff = anySuccess ? 1.0 : Math.Min(backoff * 1.5, 10.0);
                }

                var interval = Math.Max(10, settings.ReconnectIntervalSeconds) * backoff;
                if (!DelayAsync(TimeSpan.FromSeconds(interval), ct))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Never let one bad pass kill the watchdog.
                if (!DelayAsync(TimeSpan.FromSeconds(30), ct))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Attempts to reconnect every favorite device that is currently not connected.</summary>
    private async Task<bool> TryReconnectFavoritesAsync(CancellationToken ct)
    {
        List<string> favorites;
        lock (_gate)
        {
            favorites = _settings.Current.FavoriteDeviceIds.ToList();
        }

        var anySuccess = false;
        foreach (var id in favorites)
        {
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_suppressed.Contains(id))
                {
                    continue; // user disconnected it on purpose
                }
            }

            if (_bluetooth.IsConnected(id))
            {
                anySuccess = true; // at least one favorite is fine
                continue;
            }

            var name = _bluetooth.GetDeviceName(id) ?? "your device";
            var result = await _bluetooth.ConnectAsync(new AudioDeviceInfo(id, name));

            if (result == ConnectResult.Success)
            {
                anySuccess = true;
                Notify($"Audio reconnected", $"{name} is streaming to this PC again.");
            }
        }

        return anySuccess;
    }

    /// <summary>Non-throwing delay; returns false when cancellation was requested.</summary>
    private static bool DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            ct.WaitHandle.WaitOne(delay);
            return !ct.IsCancellationRequested;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Notify(string title, string message)
    {
        // H.NotifyIcon wants to be touched from the UI thread; marshal through
        // the main window's queue.
        var tray = _trayProvider();
        tray?.NotifyFromBackground(title, message);
    }
}
