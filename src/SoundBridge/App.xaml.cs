using Microsoft.UI.Dispatching;
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
        // Single instance: a second launch (e.g. clicking the autostart entry
        // while running) must not spawn a second tray icon.
        _singleInstanceMutex = new Mutex(true, @"Local\SoundBridge.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            ExitApplication();
            return;
        }

        UiDispatcher = DispatcherQueue.GetForCurrentThread();

        var startMinimizedByArg = HasStartMinimizedArg();
        var settings = SettingsService.Load();
        ApplyTheme(settings.Theme);

        BluetoothService = new A2dpSinkService();

        // Window first (the tray service marshals notifications through its
        // dispatcher queue), then tray, then the reconnect watchdog.
        MainWindow = new MainWindow();
        TrayService = new TrayService(this);
        TrayService.Initialize();

        ReconnectService = new ReconnectService(BluetoothService, SettingsService, () => TrayService);
        ReconnectService.Start();

        // Event-driven reconnect: route connection lifecycle into the watchdog.
        // Drops of favorite devices trigger an immediate retry burst; manual
        // disconnects are suppressed by MainWindow before they happen.
        BluetoothService.ConnectionStateChanged += OnBluetoothConnectionStateChanged;

        if (startMinimizedByArg || settings.StartMinimized)
        {
            // Stay in the tray; the user can open the window from the tray icon.
        }
        else
        {
            ShowMainWindow();
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

        if (!window.AppWindow.IsVisible)
        {
            window.AppWindow.Show();
        }

        if (window.AppWindow.IsMinimized)
        {
            window.AppWindow.Restore();
        }

        window.Activate();
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
        catch (Exception)
        {
            // Shutdown is best effort.
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
