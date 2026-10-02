using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Lox clones smash WearNTear on collision. Keep OnCollisionStay (ground /
    /// jump / friction) and only drop the piece-damage calls.
    /// </summary>
    internal static class CentaurHits
    {
        private static int _stay;
        private static float _slamUntil;

        public static bool IsCentaur(Character character)
        {
            return character != null && CentauroPrefab.IsOurs(character.gameObject);
        }

        public static void BeginStay(Character character)
        {
            if (!IsCentaur(character)) return;
            _stay++;
            _slamUntil = Time.time + 0.2f;
        }

        public static void EndStay(Character character)
        {
            if (!IsCentaur(character) || _stay <= 0) return;
            _stay--;
        }

        public static bool ShouldIgnore(HitData hit)
        {
            if (_stay > 0) return true;
            if (hit == null) return false;
            var attacker = hit.GetAttacker();
            if (IsCentaur(attacker)) return true;
            if (Time.time > _slamUntil) return false;
            if (attacker != null) return false;
            return hit.m_toolTier <= 0 && hit.m_damage.m_blunt > 0f;
        }
    }

    [HarmonyPatch(typeof(Character), "OnCollisionStay", typeof(Collision))]
    internal static class Character_OnCollisionStay_Centaur_Patch
    {
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Character), "OnCollisionStay", new[] { typeof(Collision) }) != null
                || AccessTools.Method(typeof(Character), "OnCollisionStay") != null;
        }

        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            CentaurHits.BeginStay(__instance);
        }

        [HarmonyFinalizer]
        private static void Finalizer(Character __instance)
        {
            CentaurHits.EndStay(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class WearNTear_Damage_Centaur_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HitData hit)
        {
            return !CentaurHits.ShouldIgnore(hit);
        }
    }

    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class WearNTear_RPC_Damage_Centaur_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(WearNTear), "RPC_Damage") != null;

        [HarmonyPrefix]
        private static bool Prefix(HitData hit)
        {
            return !CentaurHits.ShouldIgnore(hit);
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
    internal static class Destructible_Damage_Centaur_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Destructible), "Damage") != null;

        [HarmonyPrefix]
        private static bool Prefix(HitData hit)
        {
            return !CentaurHits.ShouldIgnore(hit);
        }
    }
}
