using HarmonyLib;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Same trick as the Caixa Postal: Menu.IsVisible is what frees the mouse, and the
    /// TakeInput patches stop the player/camera from moving behind the panel.
    /// </summary>
    internal static class UiInputPatches
    {
        private static bool MenuOpen => MountMenu.IsOpen || AdminMenu.IsOpen;

        [HarmonyPatch(typeof(Menu), nameof(Menu.IsVisible))]
        internal static class Menu_IsVisible_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (MenuOpen) __result = true;
            }
        }

        /// <summary>GameCamera locks the cursor to the camera every frame unless Menu.IsActive
        /// says a menu is up; IsVisible alone only shows it. Without this the camera re-locked
        /// it each frame and the menu freed it again, snapping it back to the middle of the
        /// screen: a cursor that could not be moved (same fix as NpcValheim's).</summary>
        [HarmonyPatch(typeof(Menu), nameof(Menu.IsActive))]
        internal static class Menu_IsActive_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (MenuOpen) __result = true;
            }
        }

        [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.TakeInput))]
        internal static class PlayerController_TakeInput_Patch
        {
            private static bool Prepare() => AccessTools.Method(typeof(PlayerController), "TakeInput") != null;

            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (MenuOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
        internal static class Player_TakeInput_Patch
        {
            private static bool Prepare() => AccessTools.Method(typeof(Player), "TakeInput") != null;

            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (MenuOpen) __result = false;
            }
        }
    }
}
