using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using MirageBox.Oasis.Core.DataSources;
using MirageBox.Oasis.Desktop.ViewModels;

namespace MirageBox.Oasis.Desktop.Views;

public class SourceParamTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<SourceParamKind, IDataTemplate> Templates { get; } = new();

    public bool Match(object? data) => data is SourceParamViewModel;

    public Control Build(object? data)
    {
        if (data is SourceParamViewModel vm && Templates.TryGetValue(vm.Kind, out var template))
            return template.Build(data)!;

        return new TextBlock { Text = "(unknown param kind)" };
    }
}
