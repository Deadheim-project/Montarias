using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Hud), "UpdateMount")]
    internal static class Hud_UpdateMount_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Hud), "UpdateMount") != null;

        [HarmonyPrefix]
        private static bool Prefix(Hud __instance)
        {
            var player = Player.m_localPlayer;
            if (player == null || !JavaliControl.IsRiding(player)) return true;

            var panel = Access.Get(__instance, "m_mountPanel") as GameObject;
            if (panel != null)
                panel.SetActive(false);
            return false;
        }
    }
}