using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Core.Engine;

public record ResolvedButton(string? GaugeName, ActionConfig? Action);

public class SceneManager
{
    private DeviceSceneConfig _config;
    private string _activeScene;

    public SceneManager(DeviceSceneConfig config)
    {
        _config = config;
        _activeScene = config.ActiveScene;
    }

    /// <summary>
    /// Hot-swaps the scene configuration. The active scene is kept if it still
    /// exists in the new config, otherwise falls back to the config's default.
    /// </summary>
    public void UpdateConfig(DeviceSceneConfig config)
    {
        _config = config;
        if (!config.List.ContainsKey(_activeScene))
            _activeScene = config.ActiveScene;
    }

    public string ActiveScene => _activeScene;
    public IReadOnlyCollection<string> SceneNames => _config.List.Keys;

    public void SwitchScene(string sceneNameOrDirection)
    {
        if (sceneNameOrDirection == "next" || sceneNameOrDirection == "prev")
        {
            var names = _config.List.Keys.ToList();
            if (names.Count == 0) return;
            var idx = names.IndexOf(_activeScene);
            if (sceneNameOrDirection == "next")
                idx = (idx + 1) % names.Count;
            else
                idx = (idx - 1 + names.Count) % names.Count;
            _activeScene = names[idx];
        }
        else if (_config.List.ContainsKey(sceneNameOrDirection))
        {
            _activeScene = sceneNameOrDirection;
        }
    }

    public ResolvedButton? GetButton(int buttonIndex)
    {
        var key = buttonIndex.ToString();

        if (_config.Pinned.TryGetValue(key, out var pinned))
            return new ResolvedButton(pinned.Gauge, pinned.Action);

        if (_config.List.TryGetValue(_activeScene, out var scene)
            && scene.Buttons.TryGetValue(key, out var btn))
            return new ResolvedButton(btn.Gauge, btn.Action);

        return null;
    }

    public ResolvedButton? GetTactileButton(int buttonIndex)
    {
        var key = buttonIndex.ToString();
        if (_config.List.TryGetValue(_activeScene, out var scene)
            && scene.TactileButtons?.TryGetValue(key, out var btn) == true)
            return new ResolvedButton(btn.Gauge, btn.Action);
        return null;
    }

    public ResolvedButton? GetEncoder(int encoderIndex)
    {
        var key = encoderIndex.ToString();
        if (_config.List.TryGetValue(_activeScene, out var scene)
            && scene.Encoders?.TryGetValue(key, out var btn) == true)
            return new ResolvedButton(btn.Gauge, btn.Action);
        return null;
    }

    public bool IsPinned(int buttonIndex) =>
        _config.Pinned.ContainsKey(buttonIndex.ToString());
}
