using System.Diagnostics;
using System.Text.Json;
using MirageBox.Oasis.Core.Config;
using MirageBox.Oasis.Core.DataSources;

namespace MirageBox.Oasis.Core.Engine;

public class ActionExecutor
{
    private readonly Func<string, SceneManager?> _getSceneManager;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> _dataSources;

    public ActionExecutor(Func<string, SceneManager?> getSceneManager, System.Collections.Concurrent.ConcurrentDictionary<string, IDataSource> dataSources)
    {
        _getSceneManager = getSceneManager;
        _dataSources = dataSources;
    }

    public void Execute(ActionConfig action, string deviceName)
    {
        switch (action.Type)
        {
            case "switchScene":
                var scene = GetParam(action, "scene") ?? "next";
                _getSceneManager(deviceName)?.SwitchScene(scene);
                break;

            case "launch":
                var path = GetParam(action, "path");
                if (path != null)
                {
                    var args = GetParam(action, "args") ?? "";
                    Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
                }
                break;

            case "keystrokes":
                // TODO: implement keystroke sending via platform API
                break;

            case "command":
                var cmd = GetParam(action, "command");
                if (cmd != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                        Arguments = OperatingSystem.IsWindows() ? $"/c {cmd}" : $"-c \"{cmd}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                break;

            case "dataSource":
                var sourceName = GetParam(action, "source");
                var sourceAction = GetParam(action, "action");
                if (sourceName != null && _dataSources.TryGetValue(sourceName, out var source))
                {
                    if (sourceAction is null or "__default__")
                        sourceAction = SourceActionHelper.GetDefaultAction(source.GetType());
                    if (sourceAction != null)
                    {
                        var param = GetParam(action, "param");
                        SourceActionHelper.ExecuteAction(source, sourceAction, param);
                    }
                }
                break;
        }
    }

    private static string? GetParam(ActionConfig action, string key)
    {
        if (action.Parameters == null) return null;
        return action.Parameters.TryGetValue(key, out var elem)
            ? elem.GetString()
            : null;
    }
}
