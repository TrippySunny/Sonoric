using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed partial class SyncDeviceItem : ObservableObject
{
    public SyncDeviceItem(DiscoveredSyncHost host, bool isTrusted)
    {
        Host = host;
        IsTrusted = isTrusted;
        Name = host.DisplayName;
        AddressText = host.Address.ToString();
        TrustLabel = isTrusted ? "Доверенный — PIN не нужен" : "Нужен PIN";
    }

    public DiscoveredSyncHost Host { get; }
    public string Name { get; }
    public string AddressText { get; }
    public bool IsTrusted { get; }
    public string TrustLabel { get; }
    public Action<SyncDeviceItem>? PullRequested { get; set; }

    [RelayCommand]
    private void Pull()
    {
        PullRequested?.Invoke(this);
    }
}
