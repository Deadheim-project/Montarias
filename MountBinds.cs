using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    /// <summary>
    /// Atalhos pessoais, no mesmo esquema dos pets: o jogador escolhe no menu qual montaria
    /// terrestre e qual voadora ficam "bindadas" e uma tecla para cada. A tecla invoca a
    /// bindada (de novo desmonta). Só do cliente: nada disso sincroniza nem vai ao servidor.
    /// </summary>
    internal static class MountBinds
    {
        public static ConfigEntry<string> GroundMount;
        public static ConfigEntry<string> FlyMount;
        public static ConfigEntry<KeyboardShortcut> GroundKey;
        public static ConfigEntry<KeyboardShortcut> FlyKey;

        private static KeyCode[] _allKeys;

        public static void Init(BaseUnityPlugin plugin)
        {
            GroundMount = plugin.Config.Bind("Atalhos", "MontariaTerrestre", "",
                "Id da montaria terrestre bindada (escolhida no menu). Vazio = a primeira que você puder usar.");
            FlyMount = plugin.Config.Bind("Atalhos", "MontariaVoadora", "",
                "Id da montaria voadora bindada (escolhida no menu). Vazio = a primeira que você puder usar.");
            GroundKey = plugin.Config.Bind("Atalhos", "AtalhoTerrestre", KeyboardShortcut.Empty,
                "Tecla que invoca/desmonta a montaria terrestre bindada. Vazio = sem tecla. Também se escolhe no menu de montarias.");
            FlyKey = plugin.Config.Bind("Atalhos", "AtalhoVoadora", KeyboardShortcut.Empty,
                "Tecla que invoca/desmonta a montaria voadora bindada. Vazio = sem tecla. Também se escolhe no menu de montarias.");
        }

        public static ConfigEntry<string> MountEntry(bool fly) => fly ? FlyMount : GroundMount;
        public static ConfigEntry<KeyboardShortcut> KeyEntry(bool fly) => fly ? FlyKey : GroundKey;

        public static bool IsBound(MountProfile profile) =>
            profile != null && MountEntry(profile.Flies) != null && MountEntry(profile.Flies).Value == profile.Id;

        /// <summary>Bind (ou, se já era a bindada, desfaz) da montaria na sua categoria.</summary>
        public static void Toggle(MountProfile profile)
        {
            if (profile == null) return;
            var entry = MountEntry(profile.Flies);
            if (entry == null) return;
            entry.Value = entry.Value == profile.Id ? "" : profile.Id;
            Save();
        }

        /// <summary>A montaria que a tecla chama: a bindada se ainda dá para usar, senão a primeira usável.</summary>
        public static MountProfile Resolve(bool fly)
        {
            var bound = MountSettings.ById(MountEntry(fly)?.Value);
            if (bound != null && bound.Flies == fly && MountRoster.Usable(bound)) return bound;
            foreach (var profile in MountRoster.UsableMounts())
                if (profile.Flies == fly) return profile;
            return bound != null && bound.Flies == fly ? bound : null;
        }

        public static void Tick(Player player)
        {
            if (player == null || MountMenu.IsOpen || MountMenu.Capturing || MountMenu.Typing() ||
                Console.IsVisible() || Menu.IsVisible())
                return;
            if (GroundKey != null && GroundKey.Value.IsDown()) Press(player, false);
            else if (FlyKey != null && FlyKey.Value.IsDown()) Press(player, true);
        }

        private static void Press(Player player, bool fly)
        {
            var profile = Resolve(fly);
            if (profile == null)
            {
                if (!MountRoster.Known) RiderClient.Request(true);
                player.Message(MessageHud.MessageType.Center,
                    fly ? "Você não tem nenhuma montaria voadora." : "Você não tem nenhuma montaria terrestre.");
                return;
            }
            MountControl.TrySummonProfile(player, profile);
        }

        public static void Save()
        {
            try { GroundKey?.ConfigFile.Save(); } catch { }
        }

        // ---- nome da tecla e captura (como o menu de pets) ----

        public static string KeyName(bool fly)
        {
            var entry = KeyEntry(fly);
            if (entry == null || entry.Value.MainKey == KeyCode.None) return "nenhuma";
            var sb = new StringBuilder();
            foreach (var m in entry.Value.Modifiers) sb.Append(ModName(m)).Append(" + ");
            var k = entry.Value.MainKey;
            sb.Append(k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9 ? ((int)(k - KeyCode.Alpha0)).ToString() : k.ToString());
            return sb.ToString();
        }

        private static string ModName(KeyCode m)
        {
            switch (m)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                default: return "Alt";
            }
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftShift || k == KeyCode.RightShift || k == KeyCode.LeftControl || k == KeyCode.RightControl ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.AltGr ||
            k == KeyCode.LeftCommand || k == KeyCode.RightCommand || k == KeyCode.LeftWindows || k == KeyCode.RightWindows;

        /// <summary>Captura a próxima tecla: Esc cancela, Backspace/Delete desliga. Devolve a mensagem
        /// quando a captura terminou, ou null se ainda espera uma tecla.</summary>
        public static string Capture(bool fly)
        {
            if (!Input.anyKeyDown) return null;
            if (_allKeys == null) _allKeys = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
            foreach (var k in _allKeys)
            {
                if (k == KeyCode.None || IsModifier(k) || k >= KeyCode.Mouse0 || !Input.GetKeyDown(k)) continue;
                var entry = KeyEntry(fly);
                if (k == KeyCode.Escape) return "Tecla mantida.";
                if (k == KeyCode.Backspace || k == KeyCode.Delete)
                {
                    entry.Value = KeyboardShortcut.Empty;
                    Save();
                    return "Tecla desligada.";
                }
                var mods = new List<KeyCode>();
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods.Add(KeyCode.LeftShift);
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add(KeyCode.LeftControl);
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mods.Add(KeyCode.LeftAlt);
                entry.Value = new KeyboardShortcut(k, mods.ToArray());
                Save();
                return "Tecla " + (fly ? "voadora" : "terrestre") + ": " + KeyName(fly) + ".";
            }
            return null;
        }
    }
}
