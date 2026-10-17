using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SoundBridge.Models;
using SoundBridge.Services;

namespace SoundBridge;

/// <summary>
/// Application composition root: creates services, the main window and the
/// tray icon; decides whether to show the UI or start silently in the tray.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public SettingsService SettingsService { get; } = new();
    public A2dpSinkService BluetoothService { get; private set; } = null!;
    public TrayService TrayService { get; private set; } = null!;
    public ReconnectService ReconnectService { get; private set; } = null!;
    public MainWindow? MainWindow { get; private set; }

    /// <summary>UI thread dispatcher, captured at launch; used to marshal from background threads.</summary>
    public DispatcherQueue UiDispatcher { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        BootLog.Step($"App starting v{typeof(App).Assembly.GetName().Version} — " +
            $"{Environment.OSVersion.VersionString}, {(Environment.Is64BitProcess ? "64" : "32")}-bit process");

        // Fatal-error surfacing: without these hooks, a WinUI app that throws
        // before its first window shows simply vanishes ("nothing happens").
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            BootLog.Fatal(ex, e.IsTerminating
                ? "AppDomain.UnhandledException (terminating)"
                : "AppDomain.UnhandledException");
            BootLog.ShowFatalDialog(ex?.Message ?? "Unknown fatal error.");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            BootLog.Step($"Unobserved task exception: {e.Exception.Message}");
            e.SetObserved();
        };
        this.UnhandledException += (_, e) =>
        {
            BootLog.Fatal(e.Exception, "UI thread unhandled exception");
            BootLog.ShowFatalDialog(e.Message);
            e.Handled = true; // keep the tray app alive; the error is on record
        };

        // Single instance: a second launch (e.g. clicking the autostart entry
        // while running) must not spawn a second tray icon.
        _singleInstanceMutex = new Mutex(true, @"Local\SoundBridge.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            BootLog.Step("Another instance is already running — exiting.");
            ExitApplication();
            return;
        }

        try
        {
            UiDispatcher = DispatcherQueue.GetForCurrentThread();
            BootLog.Step("Dispatcher captured");

            var startMinimizedByArg = HasStartMinimizedArg();
            var settings = SettingsService.Load();
            ApplyTheme(settings.Theme);
            BootLog.Step("Settings loaded");

            BluetoothService = new A2dpSinkService();
            BootLog.Step("Bluetooth service created");

            // Window first (the tray service marshals notifications through its
            // dispatcher queue), then tray, then the reconnect watchdog.
            MainWindow = new MainWindow();
            BootLog.Step("Main window created");

            try
            {
                TrayService = new TrayService(this);
                TrayService.Initialize();
                BootLog.Step("Tray icon created");
            }
            catch (Exception trayEx)
            {
                // The window is more important than the tray: keep going.
                BootLog.Fatal(trayEx, "Tray initialization failed (continuing)");
            }

            ReconnectService = new ReconnectService(BluetoothService, SettingsService, () => TrayService);
            ReconnectService.Start();
            BootLog.Step("Reconnect watchdog started");

            // Event-driven reconnect: route connection lifecycle into the watchdog.
            // Drops of favorite devices trigger an immediate retry burst; manual
            // disconnects are suppressed by MainWindow before they happen.
            BluetoothService.ConnectionStateChanged += OnBluetoothConnectionStateChanged;

            if (startMinimizedByArg || settings.StartMinimized)
            {
                BootLog.Step("Ready — starting minimized to tray");
            }
            else
            {
                ShowMainWindow();
                BootLog.Step("Ready — main window shown");
            }
        }
        catch (Exception ex)
        {
            BootLog.Fatal(ex, "Startup failed");
            BootLog.ShowFatalDialog(
                $"SoundBridge failed to start.\n\n{ex.GetType().Name}: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private void OnBluetoothConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.IsConnected)
        {
            ReconnectService.OnConnected(e.DeviceId);
        }
        else
        {
            ReconnectService.OnConnectionDropped(e.DeviceId);
        }
    }

    public void ShowMainWindow()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        var appWindow = window.AppWindow;
        if (!appWindow.IsVisible)
        {
            appWindow.Show();
        }

        // AppWindow has no IsMinimized/Restore members: minimize state lives on
        // the OverlappedPresenter (OverlappedPresenterState.Minimized).
        if (appWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore(true);
        }

        window.Activate();
        appWindow.MoveInZOrderAtTop(); // bring the window to the foreground
    }

    public void HideMainWindow() => MainWindow?.AppWindow?.Hide();

    public async Task ConnectLastDeviceAsync()
    {
        var id = SettingsService.Current.LastConnectedDeviceId;
        if (id is null)
        {
            TrayService.Notify("SoundBridge", "No device has been connected yet.");
            return;
        }

        if (BluetoothService.IsConnected(id))
        {
            TrayService.Notify("SoundBridge", "The last device is already connected.");
            return;
        }

        var name = BluetoothService.GetDeviceName(id) ?? "your device";
        var result = await BluetoothService.ConnectAsync(new AudioDeviceInfo(id, name));

        TrayService.Notify(result == ConnectResult.Success ? "Audio connected" : "Could not connect",
            result == ConnectResult.Success
                ? $"{name} is now streaming to this PC."
                : $"Make sure {name} is nearby and playing, then try again.");
    }

    public void ExitApplication()
    {
        try
        {
            ReconnectService?.Stop();
            BluetoothService?.DisconnectAll();
            TrayService?.Dispose();
        }
        catch (Exception ex)
        {
            BootLog.Step($"Shutdown cleanup error (ignored): {ex.Message}");
        }

        Environment.Exit(0);
    }

    private void ApplyTheme(string theme)
    {
        // Application.RequestedTheme must be set before any window content loads.
        RequestedTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => RequestedTheme, // "Auto": follow the Windows personalization setting
        };
    }

    private static bool HasStartMinimizedArg() =>
        Environment.GetCommandLineArgs().Any(a => a is "--minimized" or "-m");
}
