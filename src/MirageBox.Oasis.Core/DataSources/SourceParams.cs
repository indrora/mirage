namespace MirageBox.Oasis.Core.DataSources;

public enum SourceParamKind { Text, Number, Boolean, Choice, MultiChoice }

public record SourceParamOption(string Value, string Label);

public record SourceParamInfo(
    string Key,
    string Label,
    SourceParamKind Kind,
    string? Default = null,
    IReadOnlyList<SourceParamOption>? Options = null,
    string? Description = null);

/// <summary>
/// Implemented by data sources whose configuration the UI can edit. Called on
/// an instance (live from the engine, or transient: Create→Initialize→Start),
/// so options can reflect actual machine hardware.
///
/// MultiChoice values are persisted as a comma-joined string in
/// DataSourceConfig.Config; an empty/missing value means "all".
/// </summary>
public interface IConfigurableSource
{
    IReadOnlyList<SourceParamInfo> DescribeParameters();
}
