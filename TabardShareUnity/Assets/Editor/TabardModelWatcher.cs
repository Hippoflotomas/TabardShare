// TabardShare - rebuilds the asset bundle automatically when Blender exports a new Art/tabard.fbx.
// Unity only watches its own Assets folder, so without this nothing happens until you run
// Tools > TabardShare > Build Asset Bundle by hand. Turn it off with Tools > TabardShare > Auto Build On Export.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class TabardModelWatcher
{
    private const string MenuPath = "Tools/TabardShare/Auto Build On Export";
    private const string PrefKey = "TabardShare.AutoBuild";
    private const double PollSeconds = 1.0;
    private const double SettleSeconds = 2.0; // wait until Blender has finished writing the file

    private static double nextPoll;
    private static DateTime lastStamp = DateTime.MinValue;

    static TabardModelWatcher()
    {
        EditorApplication.update += Tick;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Debug.Log($"[TabardShare] Auto build on export is {(Enabled ? "ON" : "OFF")}.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void Tick()
    {
        if (!Enabled || EditorApplication.timeSinceStartup < nextPoll)
            return;
        nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string path = BuildTabardBundle.ExportedPath;
        if (!File.Exists(path))
            return;
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (stamp == lastStamp || (DateTime.UtcNow - stamp).TotalSeconds < SettleSeconds)
            return;
        lastStamp = stamp;

        if (!BuildTabardBundle.ExportIsNewer())
            return; // already built from this export

        Debug.Log("[TabardShare] New export of Art/tabard.fbx detected - building the asset bundle.");
        BuildTabardBundle.Build();
    }
}
