using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class SceneViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isPinned;

    public ObservableCollection<DeviceButtonViewModel> Buttons { get; } = new();
    public ObservableCollection<DeviceButtonViewModel> TactileButtons { get; } = new();
    public ObservableCollection<DeviceButtonViewModel> Encoders { get; } = new();

    public SceneViewModel(string name, bool isPinned)
    {
        _name = name;
        _isPinned = isPinned;
    }
}
