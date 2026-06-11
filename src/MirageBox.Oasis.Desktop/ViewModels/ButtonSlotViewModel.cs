using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class ButtonSlotViewModel : ViewModelBase
{
    public int Index { get; }
    public string SlotType { get; }

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string? _gaugeName;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Tile preview image (rendered gauge).</summary>
    [ObservableProperty] private Bitmap? _preview;

    /// <summary>Small line under the gauge name on tiles, e.g. the source plugin.</summary>
    [ObservableProperty] private string? _previewSourceLine;

    public SlotActionViewModel Press { get; }
    public SlotActionViewModel DoublePress { get; }
    public SlotActionViewModel Hold { get; }

    public ButtonSlotViewModel(int index, string slotType,
        Func<string, Type?>? sourceTypeResolver = null,
        System.Collections.ObjectModel.ObservableCollection<string>? sourceNames = null)
    {
        Index = index;
        SlotType = slotType;
        Press = new SlotActionViewModel(slotType == "encoder" ? "On rotate" : "Single press", sourceTypeResolver) { SourceNames = sourceNames };
        DoublePress = new SlotActionViewModel("Double press", sourceTypeResolver) { SourceNames = sourceNames };
        Hold = new SlotActionViewModel("Hold", sourceTypeResolver) { SourceNames = sourceNames };

        Press.PropertyChanged += OnActionChanged;
        DoublePress.PropertyChanged += OnActionChanged;
        Hold.PropertyChanged += OnActionChanged;
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Bubble up so the main view model's selected-slot hook (which drives
        // the live apply) and tile labels see nested action edits.
        if (e.PropertyName is nameof(SlotActionViewModel.ActionType)
            or nameof(SlotActionViewModel.ActionParam)
            or nameof(SlotActionViewModel.ActionSource)
            or nameof(SlotActionViewModel.ActionSourceAction)
            or nameof(SlotActionViewModel.ActionSourceParam))
        {
            OnPropertyChanged(nameof(ContentLabel));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasDisplay => SlotType == "display";
    public bool HasMultiPress => SlotType != "encoder";

    public bool IsEmpty => GaugeName == null && !Press.HasAction && !DoublePress.HasAction && !Hold.HasAction;

    public string TypeLabel => SlotType switch
    {
        "tactile" => "Switch",
        "encoder" => "Encoder",
        _ => "Button"
    };

    public string Title => $"{TypeLabel} {Index}";

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
            var summary = Press.Summary;
            return string.IsNullOrEmpty(summary) ? "(no action)" : summary;
        }
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
    }
}
