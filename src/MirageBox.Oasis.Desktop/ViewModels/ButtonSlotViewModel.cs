using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class ButtonSlotViewModel : ViewModelBase
{
    public int Index { get; }
    public string SlotType { get; }

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string? _gaugeName;
    [ObservableProperty] private string _actionType = "";
    [ObservableProperty] private string _actionParam = "";
    [ObservableProperty] private string _actionSource = "";
    [ObservableProperty] private string _actionSourceAction = "";
    [ObservableProperty] private string _actionSourceParam = "";
    [ObservableProperty] private bool _isSelected;

    public bool HasDisplay => SlotType == "display";
    public bool IsEmpty => GaugeName == null && !HasActionParam;
    public bool IsDataSourceAction => ActionType == "dataSource";

    public string HeaderLabel
    {
        get
        {
            var prefix = SlotType switch
            {
                "tactile" => "T",
                "encoder" => "E",
                _ => ""
            };
            var pin = IsPinned ? " 📌" : "";
            return $"{prefix}{Index}{pin}";
        }
    }

    public string ContentLabel
    {
        get
        {
            if (HasDisplay)
                return GaugeName ?? "(empty)";
            if (IsDataSourceAction && !string.IsNullOrEmpty(ActionSource))
                return $"{ActionSource}.{ActionSourceAction}";
            return HasActionParam ? ActionType : "(no action)";
        }
    }

    public bool HasActionParam => !string.IsNullOrEmpty(ActionType) && ActionType != "(none)";
    public bool HasActionTextField => HasActionParam && ActionType != "dataSource";

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
        "dataSource" => "Sends press events (short, long, encoder) to the target data source.",
        "switchScene" => "Switches the active scene on this device.",
        "launch" => "Launches an application.",
        "command" => "Runs a shell command.",
        _ => ""
    };

    public ButtonSlotViewModel(int index, string slotType)
    {
        Index = index;
        SlotType = slotType;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(GaugeName) or nameof(IsPinned))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderLabel)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ContentLabel)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsEmpty)));
        }
        if (e.PropertyName == nameof(ActionType))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasActionParam)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasActionTextField)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsDataSourceAction)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ActionParamLabel)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ActionParamPlaceholder)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ActionDescription)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ContentLabel)));
        }
        if (e.PropertyName is nameof(ActionSource) or nameof(ActionSourceAction))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ContentLabel)));
        }
    }
}
