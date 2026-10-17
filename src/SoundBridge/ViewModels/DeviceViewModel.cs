using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace SoundBridge.ViewModels;

/// <summary>
/// One row in the device dashboard. Exposes bindable status text and a status
/// color brush so the card template needs no value converters.
/// </summary>
public partial class DeviceViewModel : ObservableObject
{
    private static readonly SolidColorBrush ConnectedBrush =
        new(ColorHelper.FromArgb(255, 63, 189, 128));    // fluent green

    private static readonly SolidColorBrush DisconnectedBrush =
        new(ColorHelper.FromArgb(255, 150, 150, 150));   // neutral gray

    private static readonly SolidColorBrush BusyBrush =
        new(ColorHelper.FromArgb(255, 250, 175, 26));    // amber

    public DeviceViewModel(string id, string name)
    {
        Id = id;
        _name = name;
    }

    /// <summary>DeviceInformation id — the stable key used by every service.</summary>
    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private SolidColorBrush _statusBrush = DisconnectedBrush;

    public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

    public bool IsNotBusy => !IsBusy;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectButtonText));
        if (!IsBusy)
        {
            StatusText = value ? "Connected" : "Disconnected";
            StatusBrush = value ? ConnectedBrush : DisconnectedBrush;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        if (value)
        {
            StatusText = "Connecting…";
            StatusBrush = BusyBrush;
        }
        else
        {
            StatusText = IsConnected ? "Connected" : "Disconnected";
            StatusBrush = IsConnected ? ConnectedBrush : DisconnectedBrush;
        }
    }
}
