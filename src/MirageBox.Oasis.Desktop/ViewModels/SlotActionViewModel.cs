using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;

namespace MirageBox.Oasis.Desktop.ViewModels;

/// <summary>
/// One configurable action (single press, double press, hold, or encoder
/// rotation) of a button slot: the flattened action fields plus the
/// data-source action picker state.
/// </summary>
public partial class SlotActionViewModel : ViewModelBase
{
    private readonly Func<string, Type?>? _sourceTypeResolver;

    public string Label { get; }

    /// <summary>Data source names offered by the action editor's source picker.</summary>
    public ObservableCollection<string>? SourceNames { get; init; }

    [ObservableProperty] private string _actionType = "(none)";
    [ObservableProperty] private string _actionParam = "";
    [ObservableProperty] private string _actionSource = "";
    [ObservableProperty] private string _actionSourceAction = "";
    [ObservableProperty] private string _actionSourceParam = "";

    public ObservableCollection<SourceActionDef> SourceActions { get; } = new();

    private SourceActionDef? _selectedActionDef;
    public SourceActionDef? SelectedActionDef
    {
        get => _selectedActionDef;
        set
        {
            if (SetProperty(ref _selectedActionDef, value))
            {
                OnPropertyChanged(nameof(SelectedActionHasParam));
                if (value != null)
                    ActionSourceAction = value.Name == "" ? "" : value.Name;
            }
        }
    }

    public bool SelectedActionHasParam => SelectedActionDef?.HasParam ?? false;

    public SlotActionViewModel(string label, Func<string, Type?>? sourceTypeResolver = null)
    {
        Label = label;
        _sourceTypeResolver = sourceTypeResolver;
    }

    public bool HasAction => !string.IsNullOrEmpty(ActionType) && ActionType != "(none)";
    public bool IsDataSourceAction => ActionType == "dataSource";
    public bool HasActionTextField => HasAction && ActionType != "dataSource";

    public string Summary
    {
        get
        {
            if (!HasAction) return "";
            if (IsDataSourceAction && !string.IsNullOrEmpty(ActionSource))
                return $"{ActionSource}.{ActionSourceAction}";
            return string.IsNullOrEmpty(ActionParam) ? ActionType : $"{ActionType}: {ActionParam}";
        }
    }

    public string ActionParamLabel => ActionType switch
    {
        "switchScene" => "Scene",
        "launch" => "Application path",
        "command" => "Command",
        _ => "Parameter"
    };

    public string ActionParamPlaceholder => ActionType switch
    {
        "switchScene" => "next, prev, or scene name",
        "launch" => "e.g. taskmgr.exe, /usr/bin/htop",
        "command" => "shell command to run",
        _ => ""
    };

    public string ActionDescription => ActionType switch
    {
        "dataSource" => "Sends the press to the target data source.",
        "switchScene" => "Switches the active scene on this device.",
        "launch" => "Launches an application.",
        "command" => "Runs a shell command.",
        _ => ""
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ActionType))
        {
            foreach (var name in new[] { nameof(HasAction), nameof(IsDataSourceAction),
                nameof(HasActionTextField), nameof(ActionParamLabel),
                nameof(ActionParamPlaceholder), nameof(ActionDescription), nameof(Summary) })
                base.OnPropertyChanged(new PropertyChangedEventArgs(name));
        }
        if (e.PropertyName == nameof(ActionSource))
            RebuildSourceActions();
        if (e.PropertyName is nameof(ActionSource) or nameof(ActionSourceAction) or nameof(ActionParam))
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    private void RebuildSourceActions()
    {
        SourceActions.Clear();
        SelectedActionDef = null;

        if (string.IsNullOrEmpty(ActionSource)) return;
        var sourceType = _sourceTypeResolver?.Invoke(ActionSource);
        if (sourceType == null) return;

        var actions = SourceActionHelper.GetActionsFromAttributes(sourceType);
        SourceActions.Add(SourceActionDef.None);

        var defaultAction = actions.FirstOrDefault(a => a.IsDefault);
        if (defaultAction != null)
            SourceActions.Add(SourceActionDef.DefaultFor(defaultAction));

        foreach (var a in actions)
            SourceActions.Add(SourceActionDef.FromInfo(a));

        SelectedActionDef = string.IsNullOrEmpty(ActionSourceAction)
            ? SourceActions[0]
            : SourceActions.FirstOrDefault(a => a.Name == ActionSourceAction) ?? SourceActions[0];
    }

    public void LoadConfig(ActionConfig? config)
    {
        ActionType = string.IsNullOrEmpty(config?.Type) ? "(none)" : config.Type;
        var actionParam = "";
        var actionSource = "";
        var actionSourceAction = "";
        var actionSourceParam = "";
        if (config?.Parameters != null)
        {
            if (config.Parameters.TryGetValue("scene", out var s)) actionParam = s.GetString() ?? "";
            else if (config.Parameters.TryGetValue("path", out var p)) actionParam = p.GetString() ?? "";
            else if (config.Parameters.TryGetValue("command", out var c)) actionParam = c.GetString() ?? "";
            if (config.Parameters.TryGetValue("source", out var src)) actionSource = src.GetString() ?? "";
            if (config.Parameters.TryGetValue("action", out var act)) actionSourceAction = act.GetString() ?? "";
            if (config.Parameters.TryGetValue("param", out var prm)) actionSourceParam = prm.GetString() ?? "";
        }
        ActionSource = actionSource;
        ActionSourceAction = actionSourceAction;
        ActionSourceParam = actionSourceParam;
        ActionParam = actionParam;
        // Re-sync the picker now that both source and action are known.
        RebuildSourceActions();
    }

    public ActionConfig? ToConfig()
    {
        if (!HasAction) return null;

        var cfg = new ActionConfig { Type = ActionType, Parameters = new Dictionary<string, JsonElement>() };

        if (ActionType == "dataSource")
        {
            if (string.IsNullOrEmpty(ActionSource) || string.IsNullOrEmpty(ActionSourceAction))
                return null;
            cfg.Parameters["source"] = JsonSerializer.SerializeToElement(ActionSource);
            cfg.Parameters["action"] = JsonSerializer.SerializeToElement(ActionSourceAction);
            if (!string.IsNullOrEmpty(ActionSourceParam))
                cfg.Parameters["param"] = JsonSerializer.SerializeToElement(ActionSourceParam);
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
            cfg.Parameters[key] = JsonSerializer.SerializeToElement(ActionParam);
        }

        if (cfg.Parameters.Count == 0)
            cfg.Parameters = null;
        return cfg;
    }
}
