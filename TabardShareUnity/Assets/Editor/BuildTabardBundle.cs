// TabardShare - asset bundle builder.
// Menu: Tools > TabardShare > Build Asset Bundle
// Puts Assets/TabardShare/tabard.fbx into the "tabardshare" bundle, builds it for Windows,
// and copies the result into the plugin project (TabardShare/Assets/tabardshare) so the mod can embed it.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildTabardBundle
{
    private const string BundleName = "tabardshare";
    private static readonly string[] BundledAssets =
    {
        "Assets/TabardShare/tabard.fbx",
    };

    // The one place Blender exports to. Each build copies it into the Unity project first, so the bundle
    // can never be built from a stale copy.
    private const string ExportedModel = "Art/tabard.fbx";      // relative to the repo root
    private const string ProjectModel = "Assets/TabardShare/tabard.fbx";

    [MenuItem("Tools/TabardShare/Build Asset Bundle")]
    private static void Build()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string exported = Path.Combine(repoRoot, ExportedModel);
        string inProject = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ProjectModel));
        if (!File.Exists(exported))
        {
            Debug.LogError($"[TabardShare] {exported} not found - export the tabard from Blender to that path first.");
            return;
        }
        var exportedInfo = new FileInfo(exported);
        bool changed = !File.Exists(inProject) || !SameBytes(File.ReadAllBytes(inProject), File.ReadAllBytes(exported));
        if (changed)
        {
            File.Copy(exported, inProject, true);
            AssetDatabase.ImportAsset(ProjectModel, ImportAssetOptions.ForceUpdate);
        }
        Debug.Log($"[TabardShare] Model: {ExportedModel}, exported {exportedInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss} ({exportedInfo.Length / 1024} KB)" +
                  (changed ? " - NEW, copied into the project." : " - unchanged since the last build."));

        foreach (var path in BundledAssets)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError($"[TabardShare] Missing asset: {path}");
                return;
            }
            if (importer.assetBundleName != BundleName)
            {
                importer.assetBundleName = BundleName;
                importer.SaveAndReimport();
            }
        }

        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AssetBundles"));
        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            Debug.LogError("[TabardShare] Bundle build failed - see errors above.");
            return;
        }

        string built = Path.Combine(outDir, BundleName);
        // <repo>/TabardShareUnity/Assets -> <repo>/TabardShare/Assets
        string pluginAssets = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "TabardShare", "Assets"));
        Directory.CreateDirectory(pluginAssets);
        string dest = Path.Combine(pluginAssets, BundleName);
        File.Copy(built, dest, true);
        Debug.Log($"[TabardShare] Built '{BundleName}' ({new FileInfo(dest).Length / 1024} KB) and copied it to {dest}");
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
