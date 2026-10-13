using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// No fall damage while seated on one of our mounts. The seat moves with the body (the
    /// Moder's drops several metres from its bind pose to its resting one the moment it shows),
    /// and the game counted that as a fall from where the rider was a step earlier: a fresh
    /// character died mounting. Getting off, or being knocked off, in the air still hurts.
    /// </summary>
    [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
    internal static class Character_UpdateGroundContact_Rider_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Character), "UpdateGroundContact") != null;

        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            if (!(__instance is Player player) || player != Player.m_localPlayer) return;
            if (!(Access.Get(player, "m_attached") is bool attached) || !attached) return;
            if (!(Access.Get(player, "m_attachPoint") is Transform point) || point == null) return;
            if (point.GetComponentInParent<MountControl>() == null) return;
            Access.Set(player, "m_maxAirAltitude", player.transform.position.y);
        }
    }
}
