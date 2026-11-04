using BepInEx;
using BepInEx.Configuration;
using ServerSync;
using UnityEngine;

namespace ValheimMontarias.Pets
{
    /// <summary>
    /// Config do menu e dos bônus dos pets. Os pets em si (catálogo, itens, voo) ficam no mod
    /// Deadheim; aqui só o que o Montarias acrescenta: o menu, a tecla e os bônus do pet ativo.
    /// </summary>
    internal static class PetSettings
    {
        public static ConfigEntry<bool> BuffsEnabled;
        public static ConfigEntry<string> BuffTable;
        public static ConfigEntry<KeyboardShortcut> MenuKey;
        public static ConfigEntry<KeyboardShortcut> QuickKey;

        public static void Init(BaseUnityPlugin plugin, ConfigSync sync)
        {
            BuffsEnabled = Bind(plugin, sync, "Pets", "BonusAtivos", true,
                "Liga os bônus do pet ativo (peso, descansado, madeira, metal, coleta, ofício). Só vale com o pet invocado e fora de arena/raid.");
            BuffTable = Bind(plugin, sync, "Pets", "BonusPorPet", PetBuffs.DefaultTable,
                "Bônus de cada pet, separados por vírgula: petId=bonus:valor;bonus:valor. Valores em %. " +
                "Bônus: " + PetBuffs.KeysHelp + ". Pet sem entrada = sem bônus.");

            // Cliente apenas (não sincroniza): tecla pessoal de cada jogador.
            MenuKey = plugin.Config.Bind("Pets", "AtalhoMenuPets", KeyboardShortcut.Empty,
                "Tecla que abre o menu de pets. Vazio = sem tecla (use o console: pets, ou a Central Deadheim).");
            QuickKey = plugin.Config.Bind("Pets", "AtalhoPet", KeyboardShortcut.Empty,
                "Tecla que invoca/guarda o pet ativo rapidamente (invoca o último pet usado). Vazio = sem tecla. Também se escolhe no menu de pets.");
        }

        /// <summary>Grava o cfg do jogador na hora (a tecla escolhida no menu sobrevive a um crash).</summary>
        public static void Save()
        {
            try { QuickKey?.ConfigFile.Save(); } catch { }
        }

        private static ConfigEntry<T> Bind<T>(BaseUnityPlugin plugin, ConfigSync sync, string group, string name, T value, string description)
        {
            var entry = plugin.Config.Bind(group, name, value, description);
            sync.AddConfigEntry(entry);
            return entry;
        }
    }
}
