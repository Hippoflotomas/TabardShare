using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TabardShare
{
    /// <summary>
    ///     Draws worn tabards, on every client, from the player's ZDO ("prefab|variant").
    ///     Runs alongside VisEquipment.UpdateEquipmentVisuals, which vanilla calls every frame for each
    ///     character - the check is one string compare unless something changed.
    /// </summary>
    internal static class TabardVisuals
    {
        public const string ZdoKey = "TabardShare.Worn";

        private sealed class State
        {
            public bool Checked;
            public bool IsPlayer;
            public string Current = "";
            public bool Failed;
            public GameObject Instance;
            public int ModelIndex = -1;
        }

        private static readonly ConditionalWeakTable<VisEquipment, State> states = new ConditionalWeakTable<VisEquipment, State>();
        private static readonly FieldInfo VisNViewField = AccessTools.Field(typeof(VisEquipment), "m_nview");
        private static bool loggedError;

        private static void Refresh(VisEquipment vis)
        {
            var state = states.GetOrCreateValue(vis);
            if (!state.Checked)
            {
                state.Checked = true;
                state.IsPlayer = vis.GetComponent<Player>() != null;
            }
            if (!state.IsPlayer)
                return;

            var nview = VisNViewField != null ? VisNViewField.GetValue(vis) as ZNetView : null;
            if (nview == null || !nview.IsValid())
                return;

            // The body type can change after the tabard is drawn (character creation, or a remote player's
            // ZDO arriving late), so keep the female shape key in step with it.
            if (state.Instance != null)
            {
                int modelIndex = TabardModel.GetModelIndex(vis);
                if (modelIndex != state.ModelIndex)
                {
                    TabardModel.ApplyBodyShape(state.Instance, modelIndex);
                    state.ModelIndex = modelIndex;
                }
            }

            string wanted = nview.GetZDO().GetString(ZdoKey, "");
            bool upToDate = wanted == state.Current && (wanted.Length == 0 || state.Instance != null || state.Failed);
            if (upToDate)
                return;

            if (state.Instance != null)
                UnityEngine.Object.Destroy(state.Instance);
            state.Instance = null;
            state.Failed = false;
            state.Current = wanted;

            if (wanted.Length == 0)
            {
                TabardModel.RefreshLods(vis);
                return;
            }

            ParseWorn(wanted, out string prefabName, out int variant);
            var material = TabardItems.GetMaterial(prefabName, variant);
            if (material == null)
            {
                // A tabard from a pack we don't have: register a stand-in (and queue the notice), draw the missing pattern.
                string id = MissingTabards.IdFromPrefabName(prefabName);
                if (id != null)
                {
                    var player = vis.GetComponent<Player>();
                    MissingTabards.EnsureExists(id, player != null ? "worn by " + player.GetPlayerName() : "worn by another player");
                    material = TabardItems.GetMaterial(prefabName, variant) ?? TabardItems.GetMissingMaterial();
                }
            }
            if (material == null)
            {
                Jotunn.Logger.LogInfo($"[TabardShare] '{prefabName}' isn't a TabardShare tabard - not drawn.");
                state.Failed = true;
                return;
            }

            state.Instance = TabardModel.Attach(vis, material);
            state.Failed = state.Instance == null;
            state.ModelIndex = TabardModel.GetModelIndex(vis);
        }

        private static void ParseWorn(string value, out string prefabName, out int variant)
        {
            int bar = value.LastIndexOf('|');
            prefabName = bar >= 0 ? value.Substring(0, bar) : value;
            variant = 0;
            if (bar >= 0)
                int.TryParse(value.Substring(bar + 1), out variant);
        }

        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        private static class VisEquipment_UpdateEquipmentVisuals_Patch
        {
            private static void Postfix(VisEquipment __instance)
            {
                try
                {
                    Refresh(__instance);
                }
                catch (Exception ex)
                {
                    if (!loggedError)
                    {
                        loggedError = true;
                        Jotunn.Logger.LogError($"[TabardShare] Drawing a tabard failed (further errors hidden): {ex}");
                    }
                }
            }
        }
    }
}
