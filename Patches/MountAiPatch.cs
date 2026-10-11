using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Our mounts only ever exist with a rider on them, so their AI is replaced whole by
    /// <see cref="JavaliControl.DriveAI"/>: no passive movement of their own.
    /// </summary>
    [HarmonyPatch]
    internal static class MountAi_UpdateAI_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MonsterAI), nameof(MonsterAI.UpdateAI));
            yield return AccessTools.DeclaredMethod(typeof(AnimalAI), nameof(AnimalAI.UpdateAI));
        }

        [HarmonyPrefix]
        private static bool Prefix(BaseAI __instance, ref bool __result)
        {
            if (!__instance.TryGetComponent<JavaliControl>(out _)) return true;
            __result = JavaliControl.DriveAI(__instance);
            return false;
        }
    }
}
