using System;
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
    /// <remarks>
    /// This must never throw. MonoUpdatersExtra.UpdateAI runs every AI in one loop with no
    /// try/catch and clears its list only at the end: one exception skips the rest of
    /// MonoUpdaters.FixedUpdate (Character physics for everyone, the rider included) and
    /// leaves the list full, so a destroyed mount is called again every tick, forever.
    /// </remarks>
    [HarmonyPatch]
    internal static class MountAi_UpdateAI_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MonsterAI), nameof(MonsterAI.UpdateAI));
            yield return AccessTools.DeclaredMethod(typeof(AnimalAI), nameof(AnimalAI.UpdateAI));
        }

        private static float _lastErrorAt = -999f;

        [HarmonyPrefix]
        private static bool Prefix(BaseAI __instance, ref bool __result)
        {
            // A destroyed AI can still be in the updater's list for the tick it went away in.
            if (!__instance)
            {
                __result = false;
                return false;
            }
            if (!__instance.TryGetComponent<JavaliControl>(out _)) return true;
            try
            {
                __result = JavaliControl.DriveAI(__instance);
            }
            catch (Exception e)
            {
                __result = false;
                if (UnityEngine.Time.unscaledTime - _lastErrorAt > 5f)
                {
                    _lastErrorAt = UnityEngine.Time.unscaledTime;
                    Plugin.Log.LogWarning($"ValheimMontarias: mount AI failed: {e}");
                }
            }
            return false;
        }
    }
}
