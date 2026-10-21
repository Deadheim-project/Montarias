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
            _ = new Terminal.ConsoleCommand("montarias_fotos", "[id ...] (admin) photographs the rider on each mount", args =>
            {
                var ids = new System.Collections.Generic.List<string>();
                for (int i = 1; i < args.Args.Length; i++)
                    ids.Add(args.Args[i].Trim().ToLowerInvariant());
                Prefabs.MountPhotos.Run(ids);
            });
        }
    }
}
