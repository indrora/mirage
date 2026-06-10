using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Desktop.ViewModels;

// --- Sub ViewModels ---

public partial class DeviceViewModel : ViewModelBase
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _serial;
    [ObservableProperty] private bool _isSimulator;
    [ObservableProperty] private int _buttons = 6;
    [ObservableProperty] private int _tactile = 3;
    [ObservableProperty] private int _encoders = 0;
    [ObservableProperty] private int _imageSize = 128;
    [ObservableProperty] private string _activeScene = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private bool _hasHardwareProfile;

    public ObservableCollection<SceneViewModel> Scenes { get; } = new();

    public string DisplayName => IsSimulator
        ? $"{Name} (sim)"
        : HasHardwareProfile
            ? $"{Name} [{ProfileName}]"
            : $"{Name} [{Serial}]";

    public DeviceViewModel(string name, DeviceConfig config)
    {
        _name = name;
        _serial = config.Serial;
        _isSimulator = config.Simulator;
        _buttons = config.Buttons;
        _tactile = config.Tactile;
        _imageSize = config.ImageSize;
    }

    public void ApplyHardwareProfile(IMirageDevice device)
    {
        Buttons = device.ImageButtonCount;
        Tactile = device.TactileButtonCount;
        Encoders = device.EncoderCount;
        ImageSize = device.ImageWidth;
        ProfileName = device.Profile.Name;
        HasHardwareProfile = true;
    }

    public void LoadScenes(DeviceSceneConfig sceneConfig)
    {
        ActiveScene = sceneConfig.ActiveScene;
        Scenes.Clear();

        if (sceneConfig.Pinned.Count > 0)
        {
            var pinned = new SceneViewModel("Pinned", true);
            foreach (var (idx, btn) in sceneConfig.Pinned)
                pinned.Buttons.Add(DeviceButtonViewModel.FromConfig(idx, btn, true));
            Scenes.Add(pinned);
        }

        foreach (var (name, sc) in sceneConfig.List)
        {
            var scene = new SceneViewModel(name, false);
            foreach (var (idx, btn) in sc.Buttons)
                scene.Buttons.Add(DeviceButtonViewModel.FromConfig(idx, btn, false));
            if (sc.TactileButtons != null)
                foreach (var (idx, btn) in sc.TactileButtons)
                    scene.TactileButtons.Add(DeviceButtonViewModel.FromConfig(idx, btn, false));
            if (sc.Encoders != null)
                foreach (var (idx, btn) in sc.Encoders)
                    scene.Encoders.Add(DeviceButtonViewModel.FromConfig(idx, btn, false));
            Scenes.Add(scene);
        }
    }

    public DeviceConfig ToConfig() => new()
    {
        Serial = IsSimulator ? null : Serial,
        Simulator = IsSimulator,
        Buttons = Buttons,
        Tactile = Tactile,
        ImageSize = ImageSize
    };

    public DeviceSceneConfig ToSceneConfig()
    {
        var cfg = new DeviceSceneConfig { ActiveScene = ActiveScene };
        foreach (var scene in Scenes)
        {
            if (scene.IsPinned)
            {
                foreach (var btn in scene.Buttons)
                    cfg.Pinned[btn.ButtonIndex] = btn.ToConfig();
            }
            else
            {
                var sc = new SceneConfig();
                foreach (var btn in scene.Buttons)
                    sc.Buttons[btn.ButtonIndex] = btn.ToConfig();
                if (scene.TactileButtons.Count > 0)
                {
                    sc.TactileButtons = new Dictionary<string, ButtonAssignmentConfig>();
                    foreach (var btn in scene.TactileButtons)
                        sc.TactileButtons[btn.ButtonIndex] = btn.ToConfig();
                }
                if (scene.Encoders.Count > 0)
                {
                    sc.Encoders = new Dictionary<string, ButtonAssignmentConfig>();
                    foreach (var btn in scene.Encoders)
                        sc.Encoders[btn.ButtonIndex] = btn.ToConfig();
                }
                cfg.List[scene.Name] = sc;
            }
        }
        if (string.IsNullOrEmpty(cfg.ActiveScene) && cfg.List.Count > 0)
            cfg.ActiveScene = cfg.List.Keys.First();
        return cfg;
    }
}
