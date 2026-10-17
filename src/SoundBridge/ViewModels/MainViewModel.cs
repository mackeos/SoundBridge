using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundBridge.ViewModels;

/// <summary>Root view model for the device dashboard window.</summary>
public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty]
    private bool _hasDevices = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>True while no A2DP devices are listed — drives the first-run hint.</summary>
    public bool ShowFirstRunHint => !HasDevices;

    partial void OnHasDevicesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFirstRunHint));
    }

    public void ReplaceDevices(IEnumerable<DeviceViewModel> items)
    {
        Devices.Clear();
        foreach (var item in items)
        {
            Devices.Add(item);
        }

        HasDevices = Devices.Count > 0;
    }

    public DeviceViewModel? FindById(string deviceId) =>
        Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
}
