using HarmonyLib;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    internal static class Terminal_InitTerminal_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            _ = new Terminal.ConsoleCommand("javali", "Opens the mount menu", args =>
            {
                MountMenu.Open();
            });
            _ = new Terminal.ConsoleCommand("centauro", "Opens the mount menu", args =>
            {
                MountMenu.Open();
            });
        }
    }
}
