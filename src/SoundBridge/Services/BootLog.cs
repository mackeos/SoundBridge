using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;

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

    /// <summary>
    /// Record a fatal exception with the full inner-exception chain plus a dump
    /// of the deployment folder. A bare WinUI "XAML parsing failed." message is
    /// useless on its own: the report also answers which HResult was raised,
    /// what was actually deployed next to the exe, and which PRI files exist.
    /// </summary>
    public static void Fatal(Exception? ex, string context)
    {
        Write("FATAL", $"{context}: {ex?.GetType().Name}: {ex?.Message}");
        Write("FATAL", DescribeExceptionChain(ex));
        DescribeEnvironment();
    }

    /// <summary>
    /// Dump the deployment folder next to the running exe: the app PRI, the
    /// Windows App SDK runtime files and the WinUI framework resource PRI
    /// (control styles). A self-contained WinUI 3 app missing any of these
    /// cannot load its first window's XAML.
    /// </summary>
    public static void DescribeEnvironment()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            Write("INFO", $"Env: exe = {Environment.ProcessPath}");
            Write("INFO", $"Env: baseDir = {baseDir}");

            string[] criticalFiles =
            {
                "SoundBridge.dll",
                "SoundBridge.pri",                  // app PRI (exe-name convention)
                "resources.pri",                    // app PRI (fallback convention)
                "Microsoft.UI.Xaml.Controls.pri",   // WinUI control styles for XamlControlsResources
                "Microsoft.UI.pri",
                "Microsoft.WindowsAppRuntime.pri",
                "Microsoft.ui.xaml.dll",
                "Microsoft.UI.Xaml.Controls.dll",
                "MRM.dll",                          // resource manager that performs PRI lookup
                "Microsoft.WindowsAppRuntime.dll",
                "hostfxr.dll",                      // .NET self-contained sanity check
            };

            foreach (var file in criticalFiles)
            {
                var path = Path.Combine(baseDir, file);
                Write("INFO", $"Env: {file} => {(File.Exists(path)
                    ? $"present ({new FileInfo(path).Length} bytes)"
                    : "MISSING")}");
            }

            var priFiles = Directory.GetFiles(baseDir, "*.pri");
            Write("INFO", $"Env: PRI files in output: {(priFiles.Length == 0
                ? "NONE"
                : string.Join(", ", priFiles.Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)))}");

            var dllCount = Directory.GetFiles(baseDir, "*.dll").Length;
            var subDirs = Directory.GetDirectories(baseDir).Length;
            Write("INFO", $"Env: {dllCount} DLLs and {subDirs} subdirectories (satellite resource folders like en-us expected) in output");
        }
        catch (Exception ex)
        {
            Write("INFO", $"Env: environment dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Force the lazy WinUI resource pool (XamlControlsResources) to load and
    /// report whether the resources MainWindow.xaml asks for first are
    /// reachable - BEFORE creating any window. On a healthy install every key
    /// says present; on a broken deployment the first missing key IS the root
    /// cause of the XamlParseException that would otherwise follow.
    /// </summary>
    public static void ProbeApplicationResources()
    {
        try
        {
            if (Application.Current is null)
            {
                Write("INFO", "Resources probe: Application.Current is null - skipped");
                return;
            }

            var appResources = Application.Current.Resources;
            Write("INFO", $"Resources probe: app dictionary = {appResources.GetType().FullName}, merged dictionaries = {appResources.MergedDictionaries.Count}");
            foreach (var merged in appResources.MergedDictionaries)
            {
                Write("INFO", $"Resources probe: merged dictionary = {merged.GetType().FullName}, size = {TryGetSize(merged)}");
            }

            // The first key is app-local (App.xaml); the rest come from the
            // WinUI framework PRI and are exactly what MainWindow.xaml resolves
            // first (CaptionTextBlockStyle in the custom title bar).
            foreach (var key in new[]
                     {
                         "SubtleButtonStyle",
                         "CaptionTextBlockStyle",
                         "SymbolThemeFontFamily",
                         "ApplicationPageBackgroundThemeBrush",
                     })
            {
                Write("INFO", $"Resources probe: '{key}' => {LookupResource(appResources, key)}");
            }
        }
        catch (Exception ex)
        {
            Write("INFO", $"Resources probe: failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string LookupResource(ResourceDictionary appResources, string key)
    {
        try
        {
            if (TryLookup(appResources, key, out var value))
            {
                return $"present ({value!.GetType().Name})";
            }

            foreach (var merged in appResources.MergedDictionaries)
            {
                if (TryLookup(merged, key, out var mergedValue))
                {
                    return $"present via {merged.GetType().Name} ({mergedValue!.GetType().Name})";
                }
            }

            return "NOT FOUND";
        }
        catch (Exception ex)
        {
            return $"LOOKUP THREW {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static bool TryLookup(ResourceDictionary dictionary, object key, out object? value)
    {
        try
        {
            value = dictionary[key];
            return value is not null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static string TryGetSize(ResourceDictionary dictionary)
    {
        try
        {
            // ResourceDictionary projects IMap<object,object>; surfacing of the
            // size member varies across projections, hence the interface probe.
            if (dictionary is IDictionary<object, object> map)
            {
                return map.Count.ToString();
            }
        }
        catch
        {
            // Fall through to the placeholder below.
        }

        return "?";
    }

    private static string DescribeExceptionChain(Exception? ex)
    {
        if (ex is null)
        {
            return "(no exception object)";
        }

        var sb = new StringBuilder();
        var current = ex;
        var depth = 0;

        while (current is not null && depth < 8)
        {
            sb.AppendLine($"  [{depth}] {current.GetType().FullName} (HResult 0x{current.HResult:X8}): {current.Message}");
            if (current.StackTrace is not null)
            {
                sb.AppendLine(current.StackTrace);
            }

            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }

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
