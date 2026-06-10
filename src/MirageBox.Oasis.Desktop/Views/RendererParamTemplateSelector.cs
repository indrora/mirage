using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using MirageBox.Oasis.Desktop.ViewModels;
using MirageBox.TinyGauges;

namespace MirageBox.Oasis.Desktop.Views;

public class RendererParamTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<RendererParamKind, IDataTemplate> Templates { get; } = new();

    public bool Match(object? data) => data is RendererParamViewModel;

    public Control Build(object? data)
    {
        if (data is RendererParamViewModel vm && Templates.TryGetValue(vm.Kind, out var template))
            return template.Build(data)!;

        return new TextBlock { Text = $"(unknown param kind)" };
    }
}
