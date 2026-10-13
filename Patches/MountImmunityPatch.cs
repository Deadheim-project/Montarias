using System;
using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// A mount never takes damage, but any blow on it or on its rider knocks the rider off and
    /// the mount is gone (DerrubarAoTomarHit). Handled where the hit lands, in RPC_Damage on the
    /// owner of the one hit: for the rider that is their own client, and the mount belongs to
    /// its rider too, so the knock-off always runs where the ride can be ended.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_KnockOff_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Character), "RPC_Damage") != null;

        [HarmonyPrefix]
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance == null) return true;
            bool mount = MountHub.IsOurs(__instance.gameObject);
            var rider = Player.m_localPlayer;
            if (!mount && (rider == null || __instance != rider)) return true;

            try
            {
                if (KnocksOff(__instance, mount, rider, hit))
                {
                    MountControl.Dismount(rider);
                    rider.Message(MessageHud.MessageType.Center, "Você foi derrubado da montaria!");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: knock-off failed: {e.Message}");
            }
            return !mount;
        }

        private static bool KnocksOff(Character target, bool mount, Player rider, HitData hit)
        {
            if (hit == null || rider == null || rider.IsDead()) return false;
            if (MountSettings.KnockOffOnHit != null && !MountSettings.KnockOffOnHit.Value) return false;
            var nview = target.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;
            if (!MountControl.TryGetRidden(rider, out var ridden, out _, out _) || ridden == null) return false;
            if (mount && ridden != target) return false;
            if (!IsBlow(hit.m_hitType)) return false;

            var attacker = hit.GetAttacker();
            if (attacker == rider) return false;
            // The same rule the game keeps for the rider: a player with PvP off is not hurt by
            // other players, so they are not knocked off by them either -- on the mount neither.
            if (attacker != null && attacker.IsPlayer() && !rider.IsPVPEnabled() && !hit.m_ignorePVP) return false;
            if (!mount && hit.m_dodgeable && rider.IsDodgeInvincible()) return false;
            return true;
        }

        /// <summary>A blow from something, not the damage that keeps ticking from where you are
        /// or what you have (falling, drowning, burning, poison, cold...).</summary>
        private static bool IsBlow(HitData.HitType type)
        {
            switch (type)
            {
                case HitData.HitType.Fall:
                case HitData.HitType.Drowning:
                case HitData.HitType.Burning:
                case HitData.HitType.Freezing:
                case HitData.HitType.Poisoned:
                case HitData.HitType.Water:
                case HitData.HitType.Smoke:
                case HitData.HitType.EdgeOfWorld:
                case HitData.HitType.Self:
                case HitData.HitType.CinderFire:
                case HitData.HitType.AshlandsOcean:
                case HitData.HitType.AshlandsLava:
                    return false;
                default:
                    return true;
            }
        }
    }
}
