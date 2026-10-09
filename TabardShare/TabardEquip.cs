using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace TabardShare
{
    /// <summary>
    ///     The tabard's own equipment slot.
    ///
    ///     Tabard items are ItemType.Misc, which vanilla never equips. These patches make them equipable and
    ///     handle equip / unequip / "is it equipped?" for them, keeping the worn tabard in a TabardWearer
    ///     component on the player. Nothing else is displaced: chest, cape, utility and trinket stay as they are.
    ///     The item's m_equipped flag is saved with the inventory as usual, so vanilla re-equips it on load
    ///     through Humanoid.EquipItem, which lands here.
    /// </summary>
    internal static class TabardEquip
    {
        private static readonly MethodInfo TriggerEquipEffect = AccessTools.Method(typeof(Humanoid), "TriggerEquipEffect");

        private static void PlayEquipEffect(Humanoid humanoid, ItemDrop.ItemData item)
        {
            if (TriggerEquipEffect == null)
                return;
            try
            {
                TriggerEquipEffect.Invoke(humanoid, new object[] { item });
            }
            catch
            {
                // purely cosmetic
            }
        }

        /// <summary>Equips a tabard on a player. Returns false if it can't be equipped.</summary>
        internal static bool Equip(Player player, ItemDrop.ItemData item, bool triggerEquipEffects)
        {
            var wearer = TabardWearer.Get(player);
            if (wearer == null || wearer.Item == item)
                return false;
            if (!player.GetInventory().ContainsItem(item))
                return false;

            if (wearer.Item != null)
                Unequip(player, wearer.Item, triggerEquipEffects);

            wearer.Item = item;
            item.m_equipped = true;
            wearer.Publish();
            if (triggerEquipEffects)
                PlayEquipEffect(player, item);
            return true;
        }

        internal static void Unequip(Player player, ItemDrop.ItemData item, bool triggerEquipEffects)
        {
            var wearer = TabardWearer.Get(player);
            if (wearer == null || wearer.Item != item)
                return;

            wearer.Item = null;
            item.m_equipped = false;
            wearer.Publish();
            if (triggerEquipEffects)
                PlayEquipEffect(player, item);
        }

        // ------------------------------------------------------------------------------------------
        // Harmony patches
        // ------------------------------------------------------------------------------------------

        /// <summary>Tabards are equipable (right-click / use in the inventory toggles them).</summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.IsEquipable))]
        private static class ItemData_IsEquipable_Patch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
            {
                if (!__result && TabardItems.IsTabard(__instance))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Humanoid_EquipItem_Patch
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects, ref bool __result)
            {
                if (!TabardItems.IsTabard(item))
                    return true;
                var player = __instance as Player;
                __result = player != null && Equip(player, item, triggerEquipEffects);
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Humanoid_UnequipItem_Patch
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects)
            {
                if (!TabardItems.IsTabard(item))
                    return true;
                if (__instance is Player player)
                    Unequip(player, item, triggerEquipEffects);
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsItemEquiped))]
        private static class Humanoid_IsItemEquiped_Patch
        {
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (__result || item == null || !(__instance is Player))
                    return;
                var wearer = __instance.GetComponent<TabardWearer>();
                if (wearer != null && wearer.Item == item)
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipAllItems))]
        private static class Humanoid_UnequipAllItems_Patch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (!(__instance is Player player))
                    return;
                var wearer = player.GetComponent<TabardWearer>();
                if (wearer != null && wearer.Item != null)
                    Unequip(player, wearer.Item, false);
            }
        }
    }

    /// <summary>Holds the tabard a player is wearing and publishes it to the player's ZDO for everyone else.</summary>
    internal class TabardWearer : MonoBehaviour
    {
        public ItemDrop.ItemData Item;

        private Player player;
        private ZNetView nview;

        public static TabardWearer Get(Player player)
        {
            if (player == null)
                return null;
            var wearer = player.GetComponent<TabardWearer>();
            return wearer != null ? wearer : player.gameObject.AddComponent<TabardWearer>();
        }

        private void Awake()
        {
            player = GetComponent<Player>();
            nview = GetComponent<ZNetView>();
        }

        /// <summary>Owner only: writes "prefab|variant" (or "") to the ZDO. TabardVisuals draws from that.</summary>
        public void Publish()
        {
            if (nview == null)
                nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;
            string value = Item != null && Item.m_dropPrefab != null ? Item.m_dropPrefab.name + "|" + Item.m_variant : "";
            var zdo = nview.GetZDO();
            if (zdo.GetString(TabardVisuals.ZdoKey, "") != value)
                zdo.Set(TabardVisuals.ZdoKey, value);
        }

        private void Update()
        {
            // Safety net: if the worn tabard left the inventory by some route that didn't unequip it, take it off.
            if (Item != null && player != null && !player.GetInventory().ContainsItem(Item))
            {
                Item.m_equipped = false;
                Item = null;
                Publish();
            }
        }
    }
}
