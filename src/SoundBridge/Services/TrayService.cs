using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace SoundBridge.Services;

/// <summary>
/// Notification-area (tray) integration: persistent tray icon, context menu,
/// single-click window restore and balloon-style notifications.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly App _app;
    private TaskbarIcon? _icon;
    private ToggleMenuFlyoutItem? _startWithWindowsItem;
    private ToggleMenuFlyoutItem? _autoReconnectItem;

    public TrayService(App app) => _app = app;

    public void Initialize()
    {
        // H.NotifyIcon's WinUI TaskbarIcon has no mouse events: left-click goes
        // through LeftClickCommand and the context menu through ContextFlyout.
        _icon = new TaskbarIcon
        {
            ToolTipText = "SoundBridge — Bluetooth audio receiver",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            NoLeftClickDelay = true,
        };

        try
        {
            _icon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri("ms-appx:///Assets/app.ico"));
        }
        catch (Exception ex)
        {
            // Icon decoding failed; the tooltip and menu still work.
            BootLog.Step($"Tray icon bitmap decode failed (continuing): {ex.Message}");
        }

        _icon.LeftClickCommand = new RelayCommand(() => _app.ShowMainWindow());
        BuildContextMenu();

        // The icon is not hosted in a XAML visual tree, so it never reaches the
        // Loaded path - create the native tray icon explicitly, exactly like
        // the official H.NotifyIcon Windowless sample does.
        try
        {
            _icon.ForceCreate();
        }
        catch (Exception ex)
        {
            // Shell may be momentarily unavailable or the icon source unusable;
            // the main window must still come up. App logs the failure.
            BootLog.Fatal(ex, "Tray ForceCreate failed (continuing)");
        }
    }

    private void BuildContextMenu()
    {
        if (_icon is null)
        {
            return;
        }

        var menu = new MenuFlyout();

        menu.Items.Add(MakeItem("Open SoundBridge", () => _app.ShowMainWindow()));
        menu.Items.Add(MakeItem("Connect last device", () => _ = _app.ConnectLastDeviceAsync()));

        menu.Items.Add(new MenuFlyoutSeparator());

        _startWithWindowsItem = new ToggleMenuFlyoutItem
        {
            Text = "Start with Windows",
            IsChecked = AutoStartService.IsEnabled(),
        };
        _startWithWindowsItem.Click += (_, _) =>
        {
            var enabled = _startWithWindowsItem.IsChecked;
            AutoStartService.SetEnabled(enabled);
            _app.SettingsService.Update(s => s.StartWithWindows = enabled);
        };
        menu.Items.Add(_startWithWindowsItem);

        _autoReconnectItem = new ToggleMenuFlyoutItem
        {
            Text = "Auto-reconnect favorites",
            IsChecked = _app.SettingsService.Current.AutoReconnect,
        };
        _autoReconnectItem.Click += (_, _) =>
        {
            var enabled = _autoReconnectItem.IsChecked;
            _app.SettingsService.Update(s => s.AutoReconnect = enabled);
        };
        menu.Items.Add(_autoReconnectItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(MakeItem("Bluetooth settings…", async () =>
            await Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"))));
        menu.Items.Add(MakeItem("Exit", _app.ExitApplication));

        // FrameworkElement.ContextFlyout (FlyoutBase) - the library shows it on
        // right-click; SecondWindow mode renders it above the taskbar.
        _icon.ContextFlyout = menu;
    }

    private static MenuFlyoutItem MakeItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Keeps the tray toggle items in sync with the in-app settings.</summary>
    public void RefreshMenuState()
    {
        if (_startWithWindowsItem is not null)
        {
            _startWithWindowsItem.IsChecked = AutoStartService.IsEnabled();
        }

        if (_autoReconnectItem is not null)
        {
            _autoReconnectItem.IsChecked = _app.SettingsService.Current.AutoReconnect;
        }
    }

    /// <summary>Show a tray balloon notification (call from the UI thread).</summary>
    public void Notify(string title, string message)
    {
        try
        {
            _icon?.ShowNotification(title, message);
        }
        catch (Exception)
        {
            // Notifications are best effort.
        }
    }

    /// <summary>Marshal a notification to the UI thread (safe from the watchdog loop).</summary>
    public void NotifyFromBackground(string title, string message)
    {
        _app.UiDispatcher.TryEnqueue(() => Notify(title, message));
    }

    public void Dispose()
    {
        try
        {
            _icon?.Dispose();
        }
        catch (Exception)
        {
        }
        _icon = null;
    }
}
