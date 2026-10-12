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
            if (player == null || !MountControl.IsRiding(player)) return true;

            var panel = Access.Get(__instance, "m_mountPanel") as GameObject;
            if (panel != null)
                panel.SetActive(false);
            return false;
        }
    }

    /// <summary>
    /// The summon cast uses the game's own action bar -- the one of "equipping" or "eating" --
    /// so it looks and sits exactly like Valheim's.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateActionProgress")]
    internal static class Hud_UpdateActionProgress_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Hud), "UpdateActionProgress") != null;

        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (!MountControl.IsCasting) return;
            if (__instance.m_actionBarRoot == null || __instance.m_actionProgress == null) return;

            __instance.m_actionBarRoot.SetActive(true);
            __instance.m_actionProgress.SetValue(MountControl.CastProgress);
            if (__instance.m_actionName != null)
                __instance.m_actionName.text = string.IsNullOrEmpty(MountControl.CastName)
                    ? "Invocando montaria"
                    : $"Invocando {MountControl.CastName}";
        }
    }
}