using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundBridge.Models;
using SoundBridge.Services;
using SoundBridge.ViewModels;
using Windows.System;

namespace SoundBridge;

/// <summary>
/// The device dashboard: a Fluent, Mica-backed card list of paired A2DP
/// devices with per-device connect/disconnect, favorites and settings.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly App _app;
    private bool _settingsLoaded;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        _app = (App)Application.Current;
        InitializeComponent();

        Title = "SoundBridge";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        TryEnableMica();

        // "Close" hides to the tray (configurable), like a proper tray app.
        AppWindow.Closing += OnAppWindowClosing;

        _app.BluetoothService.ConnectionStateChanged += OnBluetoothConnectionChanged;

        LoadSettingsIntoControls();
        _ = LoadDevicesAsync();
    }

    private void TryEnableMica()
    {
        try
        {
            // Mica is a Windows 11 material; on Windows 10 the window simply
            // keeps its solid theme background.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                SystemBackdrop = new MicaBackdrop();
            }
        }
        catch (Exception)
        {
            // Backdrop unavailable — the default background is fine.
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_app.SettingsService.Current.MinimizeToTrayOnClose)
        {
            args.Cancel = true;
            _app.HideMainWindow();
        }
    }

    // ----- Device list -----------------------------------------------------

    private async Task LoadDevicesAsync()
    {
        ViewModel.StatusText = "Looking for paired audio devices…";

        try
        {
            var favorites = _app.SettingsService.Current.FavoriteDeviceIds;
            var connectedIds = _app.BluetoothService.ConnectedDeviceIds;

            var devices = await _app.BluetoothService.GetConnectableDevicesAsync();
            var cards = devices.Select(d => new DeviceViewModel(d.Id, d.Name)
            {
                IsFavorite = favorites.Contains(d.Id),
                IsConnected = connectedIds.Contains(d.Id),
            }).ToList();

            ViewModel.ReplaceDevices(cards);

            ViewModel.StatusText = devices.Count == 0
                ? "No paired audio devices found"
                : $"Ready — {devices.Count} device{(devices.Count == 1 ? "" : "s")} available";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Could not list devices: {ex.Message}";
        }
    }

    private async void OnDeviceConnectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceViewModel device })
        {
            return;
        }

        if (device.IsConnected)
        {
            // Tell the watchdog this disconnect was intentional so the
            // event-driven burst does not immediately reconnect.
            _app.ReconnectService.Suppress(device.Id);
            _app.BluetoothService.Disconnect(device.Id);
            device.IsConnected = false;
            ViewModel.StatusText = $"Disconnected from {device.Name}";
            return;
        }

        device.IsBusy = true;
        ViewModel.StatusText = $"Connecting to {device.Name}…";

        var result = await _app.BluetoothService.ConnectAsync(new AudioDeviceInfo(device.Id, device.Name));

        device.IsBusy = false;
        device.IsConnected = result is ConnectResult.Success or ConnectResult.AlreadyConnected;

        switch (result)
        {
            case ConnectResult.Success or ConnectResult.AlreadyConnected:
                _app.SettingsService.Update(s => s.LastConnectedDeviceId = device.Id);
                ViewModel.StatusText = $"Connected — play something on {device.Name}";
                _app.TrayService.Notify("Audio connected", $"{device.Name} is now streaming to this PC.");
                break;
            case ConnectResult.NotSupported:
                ViewModel.StatusText = $"{device.Name} is not available. Is it nearby and turned on?";
                break;
            case ConnectResult.TimedOut:
                ViewModel.StatusText = $"Connection to {device.Name} timed out. Try again.";
                break;
            case ConnectResult.DeniedBySystem:
                ViewModel.StatusText = "Windows denied the connection. Re-pair the device and retry.";
                break;
            default:
                ViewModel.StatusText = $"Could not connect to {device.Name}. Try again.";
                break;
        }
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceViewModel device })
        {
            return;
        }

        _app.SettingsService.Update(s =>
        {
            if (device.IsFavorite && !s.FavoriteDeviceIds.Contains(device.Id))
            {
                s.FavoriteDeviceIds.Add(device.Id);
            }
            else if (!device.IsFavorite)
            {
                s.FavoriteDeviceIds.Remove(device.Id);
            }
        });

        _app.TrayService.RefreshMenuState();
        ViewModel.StatusText = device.IsFavorite
            ? $"{device.Name} marked for automatic reconnection"
            : $"{device.Name} removed from automatic reconnection";
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadDevicesAsync();

    private async void OnPairDeviceClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    // ----- Live connection state ---------------------------------------------

    private void OnBluetoothConnectionChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        // StateChanged fires on a background thread — marshal to the UI.
        _app.UiDispatcher.TryEnqueue(() =>
        {
            var device = ViewModel.FindById(e.DeviceId);
            if (device is not null && !device.IsBusy)
            {
                device.IsConnected = e.IsConnected;
            }

            var name = device?.Name ?? _app.BluetoothService.GetDeviceName(e.DeviceId) ?? "A device";
            ViewModel.StatusText = e.IsConnected
                ? $"{name} opened an audio connection"
                : $"{name} disconnected";

            if (e.IsConnected)
            {
                _app.SettingsService.Update(s => s.LastConnectedDeviceId = e.DeviceId);
            }
        });
    }

    // ----- Settings ------------------------------------------------------------

    private void LoadSettingsIntoControls()
    {
        var s = _app.SettingsService.Current;

        StartWithWindowsToggle.IsOn = AutoStartService.IsEnabled();
        StartMinimizedToggle.IsOn = s.StartMinimized;
        AutoReconnectToggle.IsOn = s.AutoReconnect;
        MinimizeToTrayToggle.IsOn = s.MinimizeToTrayOnClose;

        ReconnectIntervalSlider.Value = s.ReconnectIntervalSeconds;
        IntervalValueText.Text = $"Checks favorite devices every {s.ReconnectIntervalSeconds} seconds";

        ThemeCombo.SelectedIndex = s.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        ApplyThemeToRoot(s.Theme);

        _settingsLoaded = true;
    }

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var enabled = StartWithWindowsToggle.IsOn;
        AutoStartService.SetEnabled(enabled);
        _app.SettingsService.Update(s => s.StartWithWindows = enabled);
        _app.TrayService.RefreshMenuState();
    }

    private void OnStartMinimizedToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var enabled = StartMinimizedToggle.IsOn;
        _app.SettingsService.Update(s => s.StartMinimized = enabled);
    }

    private void OnAutoReconnectToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var enabled = AutoReconnectToggle.IsOn;
        _app.SettingsService.Update(s => s.AutoReconnect = enabled);
        _app.TrayService.RefreshMenuState();
    }

    private void OnMinimizeToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var enabled = MinimizeToTrayToggle.IsOn;
        _app.SettingsService.Update(s => s.MinimizeToTrayOnClose = enabled);
    }

    private void OnReconnectIntervalChanged(object sender, RoutedEventArgs e)
    {
        IntervalValueText.Text = $"Checks favorite devices every {(int)ReconnectIntervalSlider.Value} seconds";

        if (!_settingsLoaded)
        {
            return;
        }

        var seconds = (int)ReconnectIntervalSlider.Value;
        _app.SettingsService.Update(s => s.ReconnectIntervalSeconds = seconds);
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var theme = ThemeCombo.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Auto",
        };

        ApplyThemeToRoot(theme);
        _app.SettingsService.Update(s => s.Theme = theme);
    }

    private void ApplyThemeToRoot(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
