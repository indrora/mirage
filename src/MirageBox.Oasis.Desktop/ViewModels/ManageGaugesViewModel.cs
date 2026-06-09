using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

public partial class ManageGaugesViewModel : ViewModelBase
{
    private readonly Dictionary<string, GaugeConfig> _gauges;

    public ObservableCollection<string> Names { get; } = new();
    public ObservableCollection<string> DataSourceNames { get; }
    public List<string> RendererTypes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedName;

    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _sensor = "";
    [ObservableProperty] private string _rendererType = "FullRing";
    [ObservableProperty] private string? _label;
    [ObservableProperty] private float _min;
    [ObservableProperty] private float _max = 100;
    [ObservableProperty] private string? _theme;

    public bool HasSelection => SelectedName != null;

    public ManageGaugesViewModel(Dictionary<string, GaugeConfig> gauges, IEnumerable<string> dataSourceNames, List<string> rendererTypes)
    {
        _gauges = gauges;
        DataSourceNames = new ObservableCollection<string>(dataSourceNames);
        RendererTypes = rendererTypes;
        foreach (var name in gauges.Keys)
            Names.Add(name);
    }

    partial void OnSelectedNameChanged(string? oldValue, string? newValue)
    {
        SaveCurrent(oldValue);
        LoadCurrent(newValue);
    }

    private void SaveCurrent(string? name)
    {
        if (name == null || !_gauges.ContainsKey(name)) return;
        var gc = _gauges[name];
        gc.Source = Source;
        gc.Sensor = Sensor;
        gc.Renderer.Type = RendererType;
        gc.Label = Label;
        gc.Min = Min;
        gc.Max = Max;
        gc.Theme = Theme;
    }

    private void LoadCurrent(string? name)
    {
        if (name == null || !_gauges.TryGetValue(name, out var gc))
        {
            Source = ""; Sensor = ""; RendererType = "FullRing";
            Label = null; Min = 0; Max = 100; Theme = null;
            return;
        }
        Source = gc.Source;
        Sensor = gc.Sensor;
        RendererType = gc.Renderer.Type;
        Label = gc.Label;
        Min = gc.Min;
        Max = gc.Max;
        Theme = gc.Theme;
    }

    [RelayCommand]
    private void Add()
    {
        var i = 1;
        while (Names.Contains($"gauge{i}")) i++;
        var name = $"gauge{i}";
        _gauges[name] = new GaugeConfig();
        Names.Add(name);
        SelectedName = name;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedName == null) return;
        var name = SelectedName;
        var idx = Names.IndexOf(name);
        _gauges.Remove(name);
        Names.Remove(name);
        SelectedName = Names.Count > 0 ? Names[Math.Min(idx, Names.Count - 1)] : null;
    }

    public void SaveAll()
    {
        SaveCurrent(SelectedName);
    }
}
