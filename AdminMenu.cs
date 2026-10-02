using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// Admin editing now lives as a tab inside the U menu. These methods keep older
    /// call sites (E na montaria, F8, RideStick) working.
    /// </summary>
    internal static class AdminMenu
    {
        public static bool IsOpen => MountMenu.AdminTabOpen;

        public static void Toggle()
        {
            if (!Access.IsAdmin())
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Apenas administradores.");
                return;
            }

            if (MountMenu.IsOpen && MountMenu.AdminTabOpen)
                MountMenu.Close();
            else
                MountMenu.OpenAdmin();
        }

        public static void Open(MountProfile profile = null) => MountMenu.OpenAdmin(profile);

        public static void Close()
        {
            if (MountMenu.AdminTabOpen)
                MountMenu.Close();
        }

        public static void Tick() { }

        public static void Draw() { }
    }
}
