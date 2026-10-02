using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Vanilla Jump/Attack unseat a saddle. Block that unseat on our boar and
    /// run jump/dash ourselves. Mounting is still Sadle.Interact (E).
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
            if (JavaliControl.TryGetRidden(player, out _, out var pet, out _))
                pet.TryDash(player);
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
            if (JavaliControl.TryGetRidden(__instance, out _, out var pet, out _))
                pet.TryDash(__instance);
            __result = true;
            return false;
        }
    }
}
