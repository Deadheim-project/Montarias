using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    internal static class Player_UseHotbarItem_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Player), "UseHotbarItem") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance, int index)
        {
            if (__instance != Player.m_localPlayer) return true;
            var item = FindHotbarItem(__instance, index);
            if (!WhistleItem.IsWhistle(item)) return true;
            __instance.Message(MessageHud.MessageType.Center, MountSettings.KeysHint("invoca") + ".");
            return false;
        }

        /// <summary>
        /// Vanilla UseHotbarItem uses 1-based slots and GetItemAt(index - 1, 0).
        /// Only that slot: scanning neighbors stole other hotbar items (belt, weapons).
        /// </summary>
        private static ItemDrop.ItemData FindHotbarItem(Player player, int index)
        {
            var inv = player.GetInventory();
            if (inv == null) return null;
            return inv.GetItemAt(Mathf.Clamp(index - 1, 0, 7), 0);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UseItem")]
    internal static class Humanoid_UseItem_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Humanoid), "UseItem") != null;

        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (!(__instance is Player player) || player != Player.m_localPlayer) return true;
            if (!WhistleItem.IsWhistle(item)) return true;
            player.Message(MessageHud.MessageType.Center, MountSettings.KeysHint("invoca") + ".");
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "UseItem")]
    internal static class Player_UseItem_Patch
    {
        private static bool Prepare() => AccessTools.DeclaredMethod(typeof(Player), "UseItem") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ItemDrop.ItemData item)
        {
            if (__instance != Player.m_localPlayer) return true;
            if (!WhistleItem.IsWhistle(item)) return true;
            __instance.Message(MessageHud.MessageType.Center, MountSettings.KeysHint("invoca") + ".");
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "TryUseItemOnInteractable")]
    internal static class Player_TryUseItemOnInteractable_Patch
    {
        private static bool Prepare() => AccessTools.DeclaredMethod(typeof(Player), "TryUseItemOnInteractable") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (__instance != Player.m_localPlayer) return true;
            if (!WhistleItem.IsWhistle(item)) return true;
            __instance.Message(MessageHud.MessageType.Center, MountSettings.KeysHint("invoca") + ".");
            __result = true;
            return false;
        }
    }
}
