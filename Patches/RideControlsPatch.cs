using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Vanilla Jump/Attack can unseat a saddle. Keep the rider attached and
    /// handle jumping ourselves. Mounting is still Sadle.Interact (E).
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateDoodadControls")]
    internal static class Player_UpdateDoodadControls_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Player), "UpdateDoodadControls") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !JavaliControl.TryOverrideRideControls(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "StopDoodadControl")]
    internal static class Player_StopDoodadControl_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Player), "StopDoodadControl") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !JavaliControl.ShouldBlockUnseat(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "AttachStop")]
    internal static class Player_AttachStop_Patch
    {
        private static bool Prepare() => AccessTools.DeclaredMethod(typeof(Player), "AttachStop") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !JavaliControl.ShouldBlockUnseat(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class Character_Jump_WhileRiding_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance)
        {
            if (!(__instance is Player player) || player != Player.m_localPlayer)
                return true;
            return !JavaliControl.IsRiding(player);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    internal static class Humanoid_StartAttack_WhileRiding_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Humanoid), "StartAttack") != null;

        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            if (!(__instance is Player player) || player != Player.m_localPlayer) return true;
            if (!JavaliControl.IsRiding(player)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "StartAttack")]
    internal static class Player_StartAttack_WhileRiding_Patch
    {
        private static bool Prepare() => AccessTools.DeclaredMethod(typeof(Player), "StartAttack") != null;

        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (__instance != Player.m_localPlayer) return true;
            if (!JavaliControl.IsRiding(__instance)) return true;
            __result = true;
            return false;
        }
    }
}
