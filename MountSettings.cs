using System;
using BepInEx;
using BepInEx.Configuration;
using ServerSync;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    internal sealed class MountProfile
    {
        public readonly string Id;
        public readonly string DefaultName;
        public readonly Func<GameObject, bool> IsInstance;
        public readonly Action ApplyAll;
        public string IconFile;
        public string PrefabName;

        public ConfigEntry<string> CustomName;
        public ConfigEntry<float> RunSpeed;
        public ConfigEntry<float> WalkSpeed;
        public ConfigEntry<float> JumpHeight;
        public ConfigEntry<float> Scale;
        public ConfigEntry<float> CastSeconds;
        public ConfigEntry<float> MaxHealth;
        public ConfigEntry<float> MaxStamina;
        public ConfigEntry<float> StaminaDrain;
        public ConfigEntry<int> ShopPrice;
        public string ShopImageFile;

        public MountProfile(string id, string defaultName, Func<GameObject, bool> isInstance, Action applyAll)
        {
            Id = id;
            DefaultName = defaultName;
            IsInstance = isInstance;
            ApplyAll = applyAll;
        }

        public string Name
        {
            get
            {
                string value = CustomName != null ? CustomName.Value : DefaultName;
                if (string.IsNullOrWhiteSpace(value))
                    return DefaultName;
                return value.Trim();
            }
        }
    }

    internal static class MountSettings
    {
        public static MountProfile Javali { get; private set; }
        public static MountProfile Centauro { get; private set; }
        public static MountProfile[] All { get; private set; }

        public static ConfigEntry<float> RunSpeed => Javali?.RunSpeed;
        public static ConfigEntry<float> WalkSpeed => Javali?.WalkSpeed;
        public static ConfigEntry<float> JumpHeight => Javali?.JumpHeight;
        public static ConfigEntry<float> Scale => Javali?.Scale;
        public static ConfigEntry<float> CastSeconds => Javali?.CastSeconds;
        public static ConfigEntry<float> MaxHealth => Javali?.MaxHealth;
        public static ConfigEntry<float> MaxStamina => Javali?.MaxStamina;
        public static ConfigEntry<float> StaminaDrain => Javali?.StaminaDrain;
        public static ConfigEntry<bool> LockConfig;
        public static ConfigEntry<bool> UnlockAll;
        public static ConfigEntry<KeyboardShortcut> MenuKey;
        public static ConfigEntry<KeyboardShortcut> SummonKey;
        public static ConfigEntry<string> SelectedMount;

        public static float Run => Javali != null ? Javali.RunSpeed.Value : 12f;
        public static float Walk => Javali != null ? Javali.WalkSpeed.Value : 5.5f;
        public static float Jump => Javali != null ? Javali.JumpHeight.Value : 8.5f;
        public static float Size => Javali != null ? Mathf.Clamp(Javali.Scale.Value, 0.5f, 3f) : 1f;
        public static float Cast => Javali != null ? Mathf.Max(0.1f, Javali.CastSeconds.Value) : 2f;
        public static float Health => Javali != null ? Mathf.Max(1f, Javali.MaxHealth.Value) : 100f;
        public static float Stamina => Javali != null ? Mathf.Max(1f, Javali.MaxStamina.Value) : 250f;
        public static float Drain => Javali != null ? Mathf.Max(0f, Javali.StaminaDrain.Value) : 8f;

        public static void Init(BaseUnityPlugin plugin, ConfigSync sync)
        {
            Javali = new MountProfile("javali", BoarPrefab.DisplayName, BoarPrefab.IsOurs, BoarPrefab.ApplyToAll)
            {
                IconFile = "Apito_Capivara.png",
                ShopImageFile = "Capivara_Shop.png",
                PrefabName = BoarPrefab.PrefabName
            };
            BindProfile(plugin, sync, Javali, "Montaria", 12f, 5.5f, 8.5f, 1f, 2f, 100f, 250f, 8f);

            Centauro = new MountProfile("centauro", CentauroPrefab.DisplayName, CentauroPrefab.IsOurs, CentauroPrefab.ApplyToAll)
            {
                IconFile = "Apito_Centauro.png",
                ShopImageFile = "Centauro_Shop.png",
                PrefabName = CentauroPrefab.PrefabName
            };
            BindProfile(plugin, sync, Centauro, "Centauro", 14f, 6f, 9f, 1f, 2f, 200f, 300f, 7f);

            All = new[] { Javali, Centauro };

            LockConfig = plugin.Config.Bind("Geral", "LockConfig", true,
                "Clientes usam a config do servidor.");
            sync.AddLockingConfigEntry(LockConfig);

            UnlockAll = Bind(plugin, sync, "Geral", "LiberarTodasGratis", false,
                "Se ligado, todas as montarias ficam disponíveis sem comprar na loja.");

            MenuKey = plugin.Config.Bind("Geral", "TeclaMenu", new KeyboardShortcut(KeyCode.U),
                "Abre o menu de montarias.");
            SummonKey = plugin.Config.Bind("Geral", "TeclaInvocar", new KeyboardShortcut(KeyCode.H),
                "Invoca ou recolhe a montaria selecionada.");
            SelectedMount = plugin.Config.Bind("Geral", "MontariaSelecionada", "javali",
                "Id da última montaria escolhida no menu.");

            if (MenuKey.Value.MainKey == KeyCode.H && SummonKey.Value.MainKey == KeyCode.J)
            {
                MenuKey.Value = new KeyboardShortcut(KeyCode.U);
                SummonKey.Value = new KeyboardShortcut(KeyCode.H);
            }
        }

        public static MountProfile Find(GameObject go)
        {
            if (go == null || All == null) return null;
            for (int i = 0; i < All.Length; i++)
            {
                var profile = All[i];
                if (profile != null && profile.IsInstance(go))
                    return profile;
            }
            return null;
        }

        private static void BindProfile(
            BaseUnityPlugin plugin, ConfigSync sync, MountProfile profile, string group,
            float run, float walk, float jump, float scale, float cast, float health, float stamina, float drain)
        {
            profile.CustomName = Bind(plugin, sync, group, "Nome", profile.DefaultName, "Nome exibido da montaria.");
            if (profile.CustomName.Value == "Javali")
                profile.CustomName.Value = profile.DefaultName;
            profile.RunSpeed = Bind(plugin, sync, group, "VelocidadeCorrida", run, "Velocidade de corrida da montaria.");
            profile.WalkSpeed = Bind(plugin, sync, group, "VelocidadeAndar", walk, "Velocidade de andar da montaria.");
            profile.JumpHeight = Bind(plugin, sync, group, "AlturaPulo", jump, "Impulso vertical do pulo da montaria.");
            profile.Scale = Bind(plugin, sync, group, "Escala", scale, "Escala da montaria (1 = tamanho vanilla).");
            profile.CastSeconds = Bind(plugin, sync, group, "CastSegundos", cast, "Tempo de conjuração para invocar.");
            profile.MaxHealth = Bind(plugin, sync, group, "Vida", health, "Vida máxima da montaria.");
            profile.MaxStamina = Bind(plugin, sync, group, "Stamina", stamina, "Stamina máxima da montaria.");
            profile.StaminaDrain = Bind(plugin, sync, group, "DrenoStamina", drain, "Stamina gasta por segundo ao correr.");
            profile.ShopPrice = Bind(plugin, sync, group, "PrecoLoja", 50, "Preço em coins na loja de montarias.");

            profile.CustomName.SettingChanged += (_, __) => profile.ApplyAll();
            profile.RunSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.WalkSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.JumpHeight.SettingChanged += (_, __) => profile.ApplyAll();
            profile.MaxHealth.SettingChanged += (_, __) => profile.ApplyAll();
            profile.MaxStamina.SettingChanged += (_, __) => profile.ApplyAll();
            profile.StaminaDrain.SettingChanged += (_, __) => profile.ApplyAll();
            profile.Scale.SettingChanged += (_, __) =>
            {
                profile.ApplyAll();
                if (Player.m_localPlayer != null)
                    BoarPrefab.KeepHumanScale(Player.m_localPlayer);
            };
        }

        private static ConfigEntry<T> Bind<T>(BaseUnityPlugin plugin, ConfigSync sync, string group, string name, T value, string description)
        {
            var entry = plugin.Config.Bind(group, name, value, description);
            sync.AddConfigEntry(entry);
            return entry;
        }
    }
}
