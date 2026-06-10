using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.DataSources;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class ChoiceItemViewModel : ViewModelBase
{
    public string Value { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    public ChoiceItemViewModel(string value, string label, bool isChecked)
    {
        Value = value;
        Label = label;
        _isChecked = isChecked;
    }
}

/// <summary>
/// Editable value for one source configuration parameter
/// (see <see cref="IConfigurableSource"/>).
/// </summary>
public partial class SourceParamViewModel : ViewModelBase
{
    public SourceParamInfo Info { get; }

    public string Key => Info.Key;
    public string Label => Info.Label;
    public SourceParamKind Kind => Info.Kind;
    public string? Description => Info.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Info.Description);
    public string Placeholder => Info.Default ?? "";

    /// <summary>Text / Number / Choice value.</summary>
    [ObservableProperty] private string? _value;

    [ObservableProperty] private bool _boolValue;

    /// <summary>MultiChoice items.</summary>
    public ObservableCollection<ChoiceItemViewModel> Choices { get; } = new();

    public IReadOnlyList<SourceParamOption> Options => Info.Options ?? [];

    public SourceParamViewModel(SourceParamInfo info, string? currentValue)
    {
        Info = info;
        var effective = currentValue ?? info.Default;

        switch (info.Kind)
        {
            case SourceParamKind.MultiChoice:
                var selected = (currentValue ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var opt in info.Options ?? [])
                    Choices.Add(new ChoiceItemViewModel(opt.Value, opt.Label, selected.Contains(opt.Value)));
                break;
            case SourceParamKind.Boolean:
                BoolValue = string.Equals(effective, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case SourceParamKind.Choice:
                Value = effective;
                break;
            default:
                Value = currentValue;
                break;
        }
    }

    /// <summary>
    /// Serialized config value, or null when the parameter is at its default
    /// (unset / all unchecked for MultiChoice) and should be omitted.
    /// </summary>
    public string? ToConfigValue()
    {
        switch (Kind)
        {
            case SourceParamKind.MultiChoice:
                var picked = Choices.Where(c => c.IsChecked).Select(c => c.Value).ToList();
                // none checked = "all" = unset; all checked is also no filter
                if (picked.Count == 0 || picked.Count == Choices.Count) return null;
                return string.Join(",", picked);
            case SourceParamKind.Boolean:
                var b = BoolValue ? "true" : "false";
                return b == (Info.Default ?? "false") ? null : b;
            default:
                var v = Value?.Trim();
                if (string.IsNullOrEmpty(v) || v == Info.Default) return null;
                return v;
        }
    }
}
