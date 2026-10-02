using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    internal static class Tameable_GetHoverText_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Tameable __instance, ref string __result)
        {
            if (!MountHub.IsOurs(__instance.gameObject)) return true;
            __result = JavaliControl.HoverText(__instance.gameObject, Player.m_localPlayer);
            return false;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class Tameable_Interact_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (!MountHub.IsOurs(__instance.gameObject)) return true;
            if (hold)
            {
                __result = false;
                return false;
            }

            var player = user as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                __result = false;
                return false;
            }

            if (!JavaliControl.IsOwner(__instance.gameObject, player))
            {
                player.Message(MessageHud.MessageType.Center, "Este animal não é seu.");
                __result = false;
                return false;
            }

            if (alt)
            {
                bool follow = !JavaliControl.IsFollowing(__instance.gameObject, player);
                JavaliControl.SetFollow(__instance.gameObject, player, follow);
                var profile = MountSettings.Find(__instance.gameObject);
                string label = profile != null ? profile.Name : "Montaria";
                player.Message(MessageHud.MessageType.Center, follow ? $"{label}: seguir" : $"{label}: ficar");
                __result = true;
                return false;
            }

            if (Access.IsAdmin())
            {
                AdminMenu.Open(MountSettings.Find(__instance.gameObject));
                __result = true;
                return false;
            }

            __result = JavaliControl.TryMount(__instance.gameObject, player);
            return false;
        }
    }
}
