using HarmonyLib;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TabardShare
{
    /// <summary>
    ///     What happens with tabards from a pack you don't have (ported from ShieldShare's MissingShields).
    ///
    ///     The game only sends item *hashes* around (chests, dropped items), so a client without the pack can't
    ///     tell which tabard it is. TabardShare therefore tags its tabards:
    ///       - every tabard item carries its ID in its custom data (saved with the item);
    ///       - a player's ZDO carries "prefab|variant" for the tabard they wear (TabardVisuals.ZdoKey);
    ///       - a dropped tabard's ZDO (and any slot an item is saved into) carries its ID.
    ///     When a client meets a tagged tabard it doesn't have, it registers a stand-in on the spot: a copy of
    ///     the built-in "missing" tabard (quarterly magenta and sable) under the tabard's ID. The item keeps its
    ///     identity - nothing is converted or deleted - and looks right again once the pack is installed.
    ///     The player gets the BannerShare/ShieldShare notice: one centre-screen message, details in chat.
    /// </summary>
    internal static class MissingTabards
    {
        public const string ItemIdKey = TabardItems.ItemIdKey;
        private const string ZdoDropKey = "TabardShare.Item";

        private static readonly Dictionary<string, GameObject> standIns = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        private static readonly FieldInfo ItemByHashField = AccessTools.Field(typeof(ObjectDB), "m_itemByHash");
        private static readonly FieldInfo NamedPrefabsField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly FieldInfo ItemStandNViewField = AccessTools.Field(typeof(ItemStand), "m_nview");

        private static Dictionary<int, GameObject> ItemByHash(ObjectDB db) => (Dictionary<int, GameObject>)ItemByHashField.GetValue(db);
        private static Dictionary<int, GameObject> NamedPrefabs(ZNetScene scene) => (Dictionary<int, GameObject>)NamedPrefabsField.GetValue(scene);

        // ------------------------------------------------------------------------------------------
        // Tags
        // ------------------------------------------------------------------------------------------

        public static string PrefabName(string id) => TabardShare.ItemPrefabPrefix + id;

        /// <summary>The tabard ID in a prefab name ("TabardShare_BlackDog" -> "BlackDog"), or null if it isn't ours.</summary>
        public static string IdFromPrefabName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || !prefabName.StartsWith(TabardShare.ItemPrefabPrefix, StringComparison.Ordinal))
                return null;
            string id = prefabName.Substring(TabardShare.ItemPrefabPrefix.Length);
            return id.Length > 0 ? id : null;
        }

        /// <summary>
        ///     ZDO key for the item saved at slot <paramref name="index"/> - mirrors ItemDrop.SaveToZDO, which is called
        ///     with -1 for dropped items and item stands, and the slot number for armour stands.
        /// </summary>
        private const int WholeObjectIndex = -1;
        private static string ItemTagKey(int index) => index < 0 ? ZdoDropKey : index + "_" + ZdoDropKey;

        /// <summary>The ID of a TabardShare item, or "" for anything else.</summary>
        public static string TagFor(ItemDrop.ItemData item)
        {
            if (item == null)
                return "";
            string id = null;
            item.m_customData?.TryGetValue(ItemIdKey, out id);
            if (string.IsNullOrEmpty(id))
                id = IdFromPrefabName(item.m_dropPrefab != null ? item.m_dropPrefab.name : null);
            return id ?? "";
        }

        private static void SetIfChanged(ZDO zdo, string key, string value)
        {
            if (zdo.GetString(key, "") != value)
                zdo.Set(key, value);
        }

        // ------------------------------------------------------------------------------------------
        // Stand-ins
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Makes sure a tabard with this ID exists in the running game, registering a stand-in if it doesn't.
        ///     Returns false if nothing could be registered.
        /// </summary>
        public static bool EnsureExists(string id, string whereSeen)
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null || string.IsNullOrEmpty(id))
                return false;

            string prefabName = PrefabName(id);
            int hash = prefabName.GetStableHashCode();
            if (ObjectDB.instance.GetItemPrefab(hash) != null)
                return true; // installed (or already stood in for this world)

            if (!standIns.TryGetValue(id, out var standIn) || standIn == null)
            {
                standIn = CreateStandIn(id);
                if (standIn == null)
                    return false;
                standIns[id] = standIn;
            }

            // Register into this world's ObjectDB and ZNetScene (both are rebuilt for every world).
            if (!ObjectDB.instance.m_items.Contains(standIn))
                ObjectDB.instance.m_items.Add(standIn);
            ItemByHash(ObjectDB.instance)[hash] = standIn;
            if (!ZNetScene.instance.m_prefabs.Contains(standIn))
                ZNetScene.instance.m_prefabs.Add(standIn);
            NamedPrefabs(ZNetScene.instance)[hash] = standIn;

            Notices.Queue(id, whereSeen);
            return true;
        }

        private static GameObject CreateStandIn(string id)
        {
            var template = PrefabManager.Instance.GetPrefab(TabardItems.MissingTemplatePrefabName);
            if (template == null)
            {
                Jotunn.Logger.LogWarning($"[TabardShare] No built-in missing tabard to stand in for '{id}'.");
                return null;
            }

            string prefabName = PrefabName(id);
            var clone = PrefabManager.Instance.CreateClonedPrefab(prefabName, template);
            var drop = clone != null ? clone.GetComponent<ItemDrop>() : null;
            if (drop == null)
                return null;

            // Own copy of the shared item data, so renaming it doesn't rename the built-in template too.
            var shared = (ItemDrop.ItemData.SharedData)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(drop.m_itemData.m_shared, null);
            shared.m_name = id + " (missing)";
            shared.m_description = $"Someone on this server has the tabard '{id}', but you don't have its TabardPack. " +
                                   "Ask them for the zip, put it in Documents\\Valheim Custom Tabards and restart the game.";
            drop.m_itemData.m_shared = shared;
            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_customData = new Dictionary<string, string> { { ItemIdKey, id } };

            TabardItems.AddStandIn(prefabName);
            Jotunn.Logger.LogInfo($"[TabardShare] Tabard '{id}' isn't installed locally - showing the missing pattern.");
            return clone;
        }

        // ------------------------------------------------------------------------------------------
        // Notices - same as BannerShare/ShieldShare: wait for things to settle, then one centre message
        // and one chat line per missing tabard.
        // ------------------------------------------------------------------------------------------

        internal static class Notices
        {
            private const float SettleSeconds = 2f;
            private static readonly HashSet<string> noticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private static readonly List<KeyValuePair<string, string>> pending = new List<KeyValuePair<string, string>>();
            private static float lastQueuedTime;

            public static void Queue(string id, string whereSeen)
            {
                if (!noticed.Add(id))
                    return;
                pending.Add(new KeyValuePair<string, string>(id, whereSeen));
                lastQueuedTime = Time.time;
            }

            public static void Update()
            {
                if (ZNet.instance == null)
                {
                    // Not in a world (main menu) - start fresh for the next one.
                    noticed.Clear();
                    pending.Clear();
                    return;
                }

                if (pending.Count == 0 || Time.time - lastQueuedTime < SettleSeconds)
                    return;

                // Things near the spawn point load before the player exists - hold on until then.
                if (Player.m_localPlayer == null || Chat.instance == null || MessageHud.instance == null)
                    return;

                string count = pending.Count == 1 ? "1 tabard file" : $"{pending.Count} tabard files";
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"You are missing {count} - check your chat log");

                foreach (var p in pending)
                    Chat.instance.AddString($"[TabardShare] Missing tabard: {p.Key}{(string.IsNullOrEmpty(p.Value) ? "" : " (" + p.Value + ")")}");
                Chat.instance.AddString("[TabardShare] Ask around the server for these TabardPacks, drop the zips in Documents\\Valheim Custom Tabards, then restart the game.");

                pending.Clear();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Harmony patches
        // ------------------------------------------------------------------------------------------

        /// <summary>Owner side: tag dropped tabards so other clients know what they are.</summary>
        [HarmonyPatch(typeof(ItemDrop), "Start")]
        private static class ItemDrop_Start_Patch
        {
            private static void Postfix(ItemDrop __instance)
            {
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                string tag = TagFor(__instance.m_itemData);
                if (tag.Length > 0)
                    SetIfChanged(nview.GetZDO(), ZdoDropKey, tag);
            }
        }

        /// <summary>Dropped tabards we don't have: register the stand-in before the game looks for the prefab.</summary>
        [HarmonyPatch(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) })]
        private static class ZNetScene_CreateObject_Patch
        {
            private static void Prefix(ZDO zdo)
            {
                if (zdo == null || ZNetScene.instance == null || ZNetScene.instance.GetPrefab(zdo.GetPrefab()) != null)
                    return;
                string id = zdo.GetString(ZdoDropKey, "");
                if (id.Length > 0 && PrefabName(id).GetStableHashCode() == zdo.GetPrefab())
                    EnsureExists(id, "on the ground");
            }
        }

        /// <summary>
        ///     Owner side: whenever an item is saved into a ZDO (dropped item, item stand, armour stand slot),
        ///     record which tabard it is - or clear the record if something else went in the slot.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), "SaveToZDO", new[] { typeof(ItemDrop.ItemData), typeof(ZDO), typeof(int) })]
        private static class ItemDrop_SaveToZDO_Patch
        {
            private static void Postfix(ItemDrop.ItemData itemData, ZDO zdo, int index)
            {
                if (zdo == null)
                    return;
                string key = ItemTagKey(index);
                string tag = TagFor(itemData);
                if (tag.Length > 0 || zdo.GetString(key, "").Length > 0)
                    SetIfChanged(zdo, key, tag);
            }
        }

        /// <summary>Item stands (wall mounts), in case a tabard can be hung on one: stand in before the stand looks it up.</summary>
        [HarmonyPatch(typeof(ItemStand), "SetVisualItem", new[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
        private static class ItemStand_SetVisualItem_Patch
        {
            private static void Prefix(ItemStand __instance, int itemHash)
            {
                var nview = (ZNetView)ItemStandNViewField.GetValue(__instance);
                if (itemHash == 0 || nview == null || !nview.IsValid() || ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(itemHash) != null)
                    return;
                string id = nview.GetZDO().GetString(ItemTagKey(WholeObjectIndex), "");
                if (id.Length > 0 && PrefabName(id).GetStableHashCode() == itemHash)
                    EnsureExists(id, "on an item stand");
            }
        }

        /// <summary>
        ///     Every inventory (player, chest, cart, ship) loads items through this overload by prefab hash.
        ///     Without a prefab the game skips the item - and it's gone for good the next time that inventory is
        ///     saved. For our tabards: register a stand-in first; and when we DO have the tabard, add the ID tag
        ///     to items made before tagging existed.
        ///
        ///     Found at startup rather than by a fixed signature (as in ShieldShare 1.0.1): the AddItem whose first
        ///     parameter is the int prefab hash and which takes the custom-data dictionary.
        /// </summary>
        [HarmonyPatch]
        private static class Inventory_AddItem_ByHash_Patch
        {
            private static MethodBase target;
            private static int hashIndex = -1, customDataIndex = -1;

            private static MethodBase FindTarget()
            {
                foreach (var m in typeof(Inventory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "AddItem")
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length < 2 || ps[0].ParameterType != typeof(int))
                        continue;
                    int dict = Array.FindIndex(ps, p => p.ParameterType == typeof(Dictionary<string, string>));
                    if (dict < 0)
                        continue;
                    hashIndex = 0;
                    customDataIndex = dict;
                    return m;
                }
                return null;
            }

            private static bool Prepare()
            {
                if (target == null)
                    target = FindTarget();
                if (target == null)
                    Jotunn.Logger.LogWarning("[TabardShare] This game build has no Inventory.AddItem(prefabHash, ..., customData, ...) - " +
                                             "tabards from missing packs in chests and inventories can't be protected.");
                return target != null;
            }

            private static MethodBase TargetMethod() => target ?? FindTarget();

            private static void Prefix(object[] __args)
            {
                int prefabHash = (int)__args[hashIndex];
                var customData = (Dictionary<string, string>)__args[customDataIndex];
                if (ObjectDB.instance == null)
                    return;
                var prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
                if (prefab != null)
                {
                    if (!TabardItems.IsTabardPrefab(prefab.name))
                        return;
                    if (customData == null)
                    {
                        customData = new Dictionary<string, string>();
                        __args[customDataIndex] = customData; // Harmony passes __args changes on to the game
                    }
                    if (!customData.ContainsKey(ItemIdKey))
                        customData[ItemIdKey] = IdFromPrefabName(prefab.name);
                    return;
                }

                if (customData == null || !customData.TryGetValue(ItemIdKey, out var id) || string.IsNullOrEmpty(id))
                    return;
                if (PrefabName(id).GetStableHashCode() != prefabHash)
                    return;
                EnsureExists(id, "in a chest or inventory");
            }
        }
    }
}
