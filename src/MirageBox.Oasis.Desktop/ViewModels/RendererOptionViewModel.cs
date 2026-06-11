namespace MirageBox.Oasis.Desktop.ViewModels;

/// <summary>
/// An entry in the gauge editor's renderer dropdown: either a builtin
/// TinyGauges style (Id == DisplayName) or the data source's own renderer
/// (Id == "__source__", badged "from sensor").
/// </summary>
public sealed record RendererOptionViewModel(string Id, string DisplayName, bool IsFromSource)
{
    public override string ToString() => DisplayName;
}
