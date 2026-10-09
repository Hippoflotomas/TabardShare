using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Reflection;

namespace TabardShare
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class TabardShare : BaseUnityPlugin
    {
        public const string PluginGUID = "com.hippotech.tabardshare";
        public const string PluginName = "TabardShare";
        public const string PluginVersion = "0.1.0";

        /// <summary>Every tabard item's prefab name starts with this.</summary>
        internal const string ItemPrefabPrefix = "TabardShare_";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private Harmony harmony;
        private ConfigEntry<bool> testTabardCraftable;

        private void Awake()
        {
            Jotunn.Logger.LogInfo("TabardShare has landed");

            testTabardCraftable = Config.Bind("Testing", "CraftableTestTabard", true,
                "Make the built-in Test Tabard (UV layout: blue front, red back) craftable at the workbench for 1 Wood. " +
                "Existing test tabards keep working either way.");

            if (!TabardModel.LoadBundle())
            {
                Jotunn.Logger.LogError("[TabardShare] The tabard model could not be loaded - the mod is disabled.");
                return;
            }

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;

            // Patches are applied one class at a time (as in ShieldShare): if a game update breaks one,
            // only that feature is lost instead of PatchAll() stopping at the first failure.
            harmony = new Harmony(PluginGUID);
            int applied = 0, failed = 0;
            foreach (var host in new[] { typeof(TabardEquip), typeof(TabardVisuals), typeof(MissingTabards) })
            {
                foreach (var type in host.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!type.IsDefined(typeof(HarmonyPatch), false))
                        continue;
                    try
                    {
                        harmony.CreateClassProcessor(type).Patch();
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Jotunn.Logger.LogWarning($"[TabardShare] Patch {host.Name}.{type.Name} could not be applied on this game build: {ex.Message}");
                    }
                }
            }
            Jotunn.Logger.LogInfo($"[TabardShare] Applied {applied} patch(es){(failed > 0 ? $", {failed} skipped" : "")}.");
        }

        private void Update()
        {
            MissingTabards.Notices.Update();
        }

        private void OnVanillaPrefabsAvailable()
        {
            // Raised from ObjectDB.CopyOtherDB, which can run more than once per session - register once.
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;

            TabardModel.SetupMaterialTemplate();
            TabardItems.RegisterTestTabard(testTabardCraftable.Value);
            TabardItems.LoadPacks();
        }
    }
}
