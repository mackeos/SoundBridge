using System.Runtime.InteropServices;

namespace SoundBridge.Services;

/// <summary>
/// Minimal startup/failure diagnostics for the unpackaged build: appends every
/// startup step and any fatal exception to %LOCALAPPDATA%\SoundBridge\boot.log,
/// and surfaces fatal errors with a native message box so a broken startup is
/// never silent (a WinUI app that dies before its first window exists would
/// otherwise look like "nothing happens" when clicking the exe).
/// </summary>
public static class BootLog
{
    private static readonly object Gate = new();
    private static bool _dialogShown;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoundBridge");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "boot.log");

    /// <summary>Record a successful startup step (or a benign, non-fatal issue).</summary>
    public static void Step(string message) => Write("INFO", message);

    /// <summary>Record a fatal exception with stack trace.</summary>
    public static void Fatal(Exception? ex, string context) =>
        Write("FATAL", $"{context}: {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}");

    /// <summary>
    /// Show a native error dialog with the log location. user32 MessageBoxW is
    /// used deliberately: it works before any WinUI window (or dispatcher) exists.
    /// Shown at most once per process to avoid dialog storms.
    /// </summary>
    public static void ShowFatalDialog(string message)
    {
        if (_dialogShown)
        {
            return;
        }

        _dialogShown = true;
        _ = MessageBoxW(
            IntPtr.Zero,
            $"{message}\n\nDetails were written to:\n{LogPath}",
            "SoundBridge — startup error",
            0x00000010); // MB_ICONERROR
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Diagnostics must never be the reason an app fails.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
