using System;
using System.Collections.Generic;
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
        public string IconPrefab;
        public string VisualAsset;
        public string PrefabName;
        /// <summary>The mount item (its whistle): using it mounts and dismounts.</summary>
        public string ItemPrefab;

        public ConfigEntry<string> CustomName;
        public ConfigEntry<float> RunSpeed;
        public ConfigEntry<float> WalkSpeed;
        public ConfigEntry<float> JumpHeight;
        public ConfigEntry<float> Scale;
        public ConfigEntry<float> SeatOffset;
        public ConfigEntry<float> SeatXOffset;
        public ConfigEntry<float> SeatZOffset;
        public ConfigEntry<float> SaddleXOffset;
        public ConfigEntry<float> SaddleYOffset;
        public ConfigEntry<float> SaddleZOffset;
        public ConfigEntry<string> SaddlePrefab;
        public ConfigEntry<float> CastSeconds;
        public ConfigEntry<int> RequiredRank;
        public ConfigEntry<string> AllowedSteamIds;

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

        /// <summary>Riding skill level needed to summon it (1 = the first level).</summary>
        public int MinRank => RequiredRank != null ? Mathf.Max(1, RequiredRank.Value) : 1;
    }

    internal static class MountSettings
    {
        public static MountProfile Javali { get; private set; }
        public static MountProfile[] All { get; private set; }

        public static ConfigEntry<float> RunSpeed => Javali?.RunSpeed;
        public static ConfigEntry<float> WalkSpeed => Javali?.WalkSpeed;
        public static ConfigEntry<float> JumpHeight => Javali?.JumpHeight;
        public static ConfigEntry<float> Scale => Javali?.Scale;
        public static ConfigEntry<float> SeatOffset => Javali?.SeatOffset;
        public static ConfigEntry<float> CastSeconds => Javali?.CastSeconds;
        public static ConfigEntry<bool> LockConfig;
        public static ConfigEntry<bool> UnlockAll;
        public static ConfigEntry<KeyboardShortcut> MenuKey;
        public static ConfigEntry<string> RidingRankList;

        public static float Run => Javali != null ? Javali.RunSpeed.Value : 12f;
        public static float Walk => Javali != null ? Javali.WalkSpeed.Value : 5.5f;
        public static float Jump => Javali != null ? Javali.JumpHeight.Value : 8.5f;
        public static float Size => Javali != null ? Mathf.Clamp(Javali.Scale.Value, 0.5f, 3f) : 1f;
        public static float SeatHeightOffset => Javali != null ? Mathf.Clamp(Javali.SeatOffset.Value, -2f, 2f) : 0f;
        public static Vector3 SeatPositionOffset => Javali != null
            ? new Vector3(Javali.SeatXOffset.Value, 0f, Javali.SeatZOffset.Value)
            : Vector3.zero;
        public static Vector3 SaddlePositionOffset => Javali != null
            ? new Vector3(Javali.SaddleXOffset.Value, Javali.SaddleYOffset.Value, Javali.SaddleZOffset.Value)
            : Vector3.zero;
        public static float Cast => Javali != null ? Mathf.Max(0.1f, Javali.CastSeconds.Value) : 2f;

        public static void Init(BaseUnityPlugin plugin, ConfigSync sync)
        {
            var profiles = new List<MountProfile>();
            profiles.Add(CreateProfile("capivara", "Capivara", "CapivaraMontaria", "ApitoCapivara", "apito_capivara.png", "TrophyBoar"));
            profiles.Add(CreateProfile("neck", "Neck", "NeckMontaria", "ApitoNeck", null, "TrophyNeck"));
            profiles.Add(CreateProfile("deer", "Deer", "DeerMontaria", "ApitoDeer", null, "TrophyDeer"));
            profiles.Add(CreateProfile("boar", "Boar", "BoarMontaria", "ApitoBoar", null, "TrophyBoar"));
            profiles.Add(CreateProfile("eikthyr", "Eikthyr", "EikthyrMontaria", "ApitoEikthyr", null, "TrophyEikthyr"));
            All = profiles.ToArray();
            Javali = All.Length > 0 ? All[0] : null;

            foreach (var profile in All)
                BindProfile(plugin, sync, profile, "Montaria" + profile.Id, 12f, 5.5f, 8.5f, 1f, 2f);

            LockConfig = plugin.Config.Bind("Geral", "LockConfig", true,
                "Clientes usam a config do servidor.");
            sync.AddLockingConfigEntry(LockConfig);

            UnlockAll = Bind(plugin, sync, "Geral", "LiberarTodasGratis", false,
                "Se ligado, todo mundo tem a habilidade máxima e todas as montarias, sem comprar.");

            RidingRankList = Bind(plugin, sync, "Habilidade", "Niveis", RidingRanks.Default,
                "Níveis da Habilidade de Montaria, em ordem: nome:velocidade separados por |. " +
                "A velocidade multiplica a da montaria de quem tem o nível (1 = sem bônus). " +
                "Quem vende é o Mestre das Montarias (NpcValheim).");
            RidingRankList.SettingChanged += (_, __) => MountHub.ApplySpeedAll();

            MenuKey = plugin.Config.Bind("Geral", "TeclaMenu", new KeyboardShortcut(KeyCode.U),
                "Abre o menu de montarias.");
        }

        public static string MenuKeyName() => MenuKey != null ? MenuKey.Value.MainKey.ToString() : "U";

        public static MountProfile ByItem(string itemPrefab)
        {
            if (string.IsNullOrEmpty(itemPrefab) || All == null) return null;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i] != null && All[i].ItemPrefab == itemPrefab)
                    return All[i];
            }
            return null;
        }

        public static MountProfile ById(string id)
        {
            if (string.IsNullOrEmpty(id) || All == null) return null;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i] != null && All[i].Id == id)
                    return All[i];
            }
            return null;
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

        private static MountProfile CreateProfile(string id, string defaultName, string prefabName, string itemPrefab, string iconFile, string iconPrefab)
        {
            MountProfile profile = null;
            profile = new MountProfile(id, defaultName,
                go => go != null && go.name.StartsWith(prefabName, StringComparison.Ordinal),
                () => BoarPrefab.ApplyProfile(profile));
            profile.IconFile = iconFile;
            profile.IconPrefab = iconPrefab;
            profile.VisualAsset = string.IsNullOrEmpty(id)
                ? "capybara"
                : id switch
                {
                    "capivara" => "capybara",
                    "neck" => "neck",
                    "deer" => "deer",
                    "boar" => "boar",
                    "eikthyr" => "eikthyr",
                    _ => "capybara"
                };
            profile.PrefabName = prefabName;
            profile.ItemPrefab = itemPrefab;
            return profile;
        }

        private static void BindProfile(
            BaseUnityPlugin plugin, ConfigSync sync, MountProfile profile, string group,
            float run, float walk, float jump, float scale, float cast)
        {
            profile.CustomName = Bind(plugin, sync, group, "Nome", profile.DefaultName, "Nome exibido da montaria.");
            if (profile.CustomName.Value == "Javali")
                profile.CustomName.Value = profile.DefaultName;
            profile.RunSpeed = Bind(plugin, sync, group, "VelocidadeCorrida", run, "Velocidade de corrida da montaria.");
            profile.WalkSpeed = Bind(plugin, sync, group, "VelocidadeAndar", walk, "Velocidade de andar da montaria.");
            profile.JumpHeight = Bind(plugin, sync, group, "AlturaPulo", jump, "Impulso vertical do pulo da montaria.");
            profile.Scale = Bind(plugin, sync, group, "Escala", scale, "Escala da montaria (1 = tamanho vanilla).");
            profile.SeatOffset = Bind(plugin, sync, group, "AlturaAssento", 0f,
                "Ajuste vertical do personagem ao montar (positivo sobe, negativo desce).");
            profile.SeatXOffset = Bind(plugin, sync, group, "AssentoOffsetX", 0f,
                "Deslocamento lateral do personagem sobre a montaria, em metros.");
            profile.SeatZOffset = Bind(plugin, sync, group, "AssentoOffsetZ", 0f,
                "Deslocamento frente/trás do personagem sobre a montaria, em metros.");
            profile.SaddleXOffset = Bind(plugin, sync, group, "SelaOffsetX", 0f,
                "Deslocamento lateral da sela em unidades do modelo.");
            profile.SaddleYOffset = Bind(plugin, sync, group, "SelaOffsetY", 0f,
                "Deslocamento vertical da sela em unidades do modelo.");
            profile.SaddleZOffset = Bind(plugin, sync, group, "SelaOffsetZ", 0f,
                "Deslocamento frente/trás da sela em unidades do modelo.");
            profile.SaddlePrefab = Bind(plugin, sync, group, "PrefabSela", "SaddleLox",
                "Prefab de item da sela usado na aparência da montaria (ex.: SaddleLox, SaddleAsksvin).");
            profile.CastSeconds = Bind(plugin, sync, group, "CastSegundos", cast, "Tempo de conjuração para invocar.");
            profile.RequiredRank = Bind(plugin, sync, group, "HabilidadeMinima", 1,
                "Nível da Habilidade de Montaria exigido para invocar (1 = o primeiro nível).");
            if (profile.Id == "capivara")
            {
                profile.AllowedSteamIds = Bind(plugin, sync, group, "SteamIdsLiberados", "",
                    "Lista de SteamIDs permitidos para esta montaria. Separe por vírgula. Vazio = qualquer um.");
            }

            profile.CustomName.SettingChanged += (_, __) => profile.ApplyAll();
            profile.RunSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.WalkSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.JumpHeight.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SeatOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SeatXOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SeatZOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleXOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleYOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleZOffset.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddlePrefab.SettingChanged += (_, __) => profile.ApplyAll();
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
