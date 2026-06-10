using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Engine;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class RendererParamViewModel : ViewModelBase
{
    public string Key { get; }
    public string Description { get; }
    public RendererParamKind Kind { get; }
    public string? Default { get; }

    [ObservableProperty] private string _value = "";

    public bool HasDefault => Default != null;

    /// <summary>True when the current value matches the declared default.</summary>
    public bool IsDefault => HasDefault && string.Equals(Value, Default, StringComparison.Ordinal);

    /// <summary>Label shown above the field, includes default hint when one exists.</summary>
    public string Label => HasDefault ? $"{Description} (default: {Default})" : Description;

    public string Placeholder => Default ?? "";

    /// <summary>
    /// Two-way bool wrapper for Boolean params.
    /// </summary>
    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set
        {
            Value = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    public RendererParamViewModel(RendererParamInfo info, string? currentValue = null)
    {
        Key = info.Key;
        Description = info.Description;
        Kind = info.Kind;
        Default = info.Default;
        _value = currentValue ?? "";
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsDefault));
        if (Kind == RendererParamKind.Boolean)
            OnPropertyChanged(nameof(BoolValue));
    }

    /// <summary>
    /// Resets the value to the declared default (or empty if none).
    /// </summary>
    public void ResetToDefault() => Value = Default ?? "";
}
