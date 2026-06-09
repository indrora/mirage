using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class DeviceButtonViewModel : ViewModelBase
{
    [ObservableProperty] private string _buttonIndex;
    [ObservableProperty] private string? _gauge;
    [ObservableProperty] private string _actionType = "";
    [ObservableProperty] private string _actionParam = "";
    [ObservableProperty] private string _actionSource = "";
    [ObservableProperty] private string _actionSourceAction = "";
    [ObservableProperty] private string _actionSourceParam = "";
    [ObservableProperty] private bool _isPinned;

    public DeviceButtonViewModel(string buttonIndex, string? gauge, string actionType, string actionParam,
        string actionSource, string actionSourceAction, string actionSourceParam, bool isPinned)
    {
        _buttonIndex = buttonIndex;
        _gauge = gauge;
        _actionType = actionType;
        _actionParam = actionParam;
        _actionSource = actionSource;
        _actionSourceAction = actionSourceAction;
        _actionSourceParam = actionSourceParam;
        _isPinned = isPinned;
    }

    public static DeviceButtonViewModel FromConfig(string index, ButtonAssignmentConfig config, bool isPinned)
    {
        var actionType = config.Action?.Type ?? "";
        var actionParam = "";
        var actionSource = "";
        var actionSourceAction = "";
        var actionSourceParam = "";
        if (config.Action?.Parameters != null)
        {
            if (config.Action.Parameters.TryGetValue("scene", out var s)) actionParam = s.GetString() ?? "";
            else if (config.Action.Parameters.TryGetValue("path", out var p)) actionParam = p.GetString() ?? "";
            else if (config.Action.Parameters.TryGetValue("command", out var c)) actionParam = c.GetString() ?? "";
            if (config.Action.Parameters.TryGetValue("source", out var src)) actionSource = src.GetString() ?? "";
            if (config.Action.Parameters.TryGetValue("action", out var act)) actionSourceAction = act.GetString() ?? "";
            if (config.Action.Parameters.TryGetValue("param", out var prm)) actionSourceParam = prm.GetString() ?? "";
        }
        return new DeviceButtonViewModel(index, config.Gauge, actionType, actionParam,
            actionSource, actionSourceAction, actionSourceParam, isPinned);
    }

    public ButtonAssignmentConfig ToConfig()
    {
        var cfg = new ButtonAssignmentConfig { Gauge = Gauge };
        if (!string.IsNullOrEmpty(ActionType) && ActionType != "(none)")
        {
            cfg.Action = new ActionConfig { Type = ActionType };
            cfg.Action.Parameters = new Dictionary<string, JsonElement>();

            if (ActionType == "dataSource")
            {
                if (string.IsNullOrEmpty(ActionSource) || string.IsNullOrEmpty(ActionSourceAction))
                {
                    cfg.Action = null;
                }
                else
                {
                    cfg.Action.Parameters["source"] = JsonSerializer.SerializeToElement(ActionSource);
                    cfg.Action.Parameters["action"] = JsonSerializer.SerializeToElement(ActionSourceAction);
                    if (!string.IsNullOrEmpty(ActionSourceParam))
                        cfg.Action.Parameters["param"] = JsonSerializer.SerializeToElement(ActionSourceParam);
                }
            }
            else if (!string.IsNullOrEmpty(ActionParam))
            {
                var key = ActionType switch
                {
                    "switchScene" => "scene",
                    "launch" => "path",
                    "command" => "command",
                    _ => "value"
                };
                cfg.Action.Parameters[key] = JsonSerializer.SerializeToElement(ActionParam);
            }

            if (cfg.Action?.Parameters?.Count == 0)
                cfg.Action.Parameters = null;
        }
        return cfg;
    }
}
