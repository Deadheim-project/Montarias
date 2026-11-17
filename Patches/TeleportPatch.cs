using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Teleportar montado bugava: Player.TeleportTo/UpdateTeleport movem só o jogador, o assento
    /// continua preso à montaria (outro objeto de rede, que fica para trás) e puxa o jogador de
    /// volta. Antes do teleporte o cavaleiro desmonta; ao chegar a montaria é invocada de novo.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo), new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) })]
    internal static class Player_TeleportTo_Mount_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance)
        {
            try { MountControl.BeforeTeleport(__instance); }
            catch (System.Exception e) { Plugin.Log.LogWarning("ValheimMontarias: teleport dismount failed: " + e.Message); }
        }
    }
}
