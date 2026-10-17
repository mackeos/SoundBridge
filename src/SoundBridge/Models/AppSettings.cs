namespace SoundBridge.Models;

/// <summary>
/// User preferences, persisted as JSON under %LOCALAPPDATA%\SoundBridge\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Register the app in the per-user Windows "Run" key so it launches at sign-in.</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>Launch hidden in the notification area instead of showing the main window.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Periodically watch favorite devices and re-establish audio when they drop or come back in range.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>How often the reconnect watchdog polls favorite devices, in seconds.</summary>
    public int ReconnectIntervalSeconds { get; set; } = 30;

    /// <summary>Clicking the window close button hides to the tray instead of exiting.</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>UI theme: "Auto" (follow Windows), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "Auto";

    /// <summary>DeviceInformation ids the user starred for automatic reconnection.</summary>
    public List<string> FavoriteDeviceIds { get; set; } = new();

    /// <summary>Most recently connected device (used by the tray "Connect last device" action).</summary>
    public string? LastConnectedDeviceId { get; set; }
}
