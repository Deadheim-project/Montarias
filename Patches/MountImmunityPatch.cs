using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Character), "Damage")]
    internal static class Character_Damage_MountImmunity_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Character), "Damage") != null;

        [HarmonyPrefix]
        private static bool Prefix(Character __instance)
        {
            return __instance == null || !MountHub.IsOurs(__instance.gameObject);
        }
    }
}