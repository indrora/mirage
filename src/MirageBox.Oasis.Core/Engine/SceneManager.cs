using MirageBox.Oasis.Core.Config;

namespace MirageBox.Oasis.Core.Engine;

public record ResolvedButton(string? GaugeName, ActionConfig? Action);

public class SceneManager
{
    private readonly DeviceSceneConfig _config;
    private string _activeScene;

    public SceneManager(DeviceSceneConfig config)
    {
        _config = config;
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

    public bool IsPinned(int buttonIndex) =>
        _config.Pinned.ContainsKey(buttonIndex.ToString());
}
