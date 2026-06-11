using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

/// <summary>
/// Persistence-shaped view of one button assignment within a scene: gauge +
/// the three press actions, carried as raw ActionConfig objects.
/// </summary>
public partial class DeviceButtonViewModel : ViewModelBase
{
    [ObservableProperty] private string _buttonIndex;
    [ObservableProperty] private string? _gauge;
    [ObservableProperty] private bool _isPinned;

    public ActionConfig? Action { get; set; }
    public ActionConfig? DoublePressAction { get; set; }
    public ActionConfig? HoldAction { get; set; }

    public DeviceButtonViewModel(string buttonIndex, string? gauge,
        ActionConfig? action, ActionConfig? doublePressAction, ActionConfig? holdAction, bool isPinned)
    {
        _buttonIndex = buttonIndex;
        _gauge = gauge;
        Action = action;
        DoublePressAction = doublePressAction;
        HoldAction = holdAction;
        _isPinned = isPinned;
    }

    public static DeviceButtonViewModel FromConfig(string index, ButtonAssignmentConfig config, bool isPinned)
        => new(index, config.Gauge, config.Action, config.DoublePressAction, config.HoldAction, isPinned);

    public ButtonAssignmentConfig ToConfig() => new()
    {
        Gauge = Gauge,
        Action = Action,
        DoublePressAction = DoublePressAction,
        HoldAction = HoldAction,
    };
}
