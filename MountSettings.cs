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
        public ConfigEntry<float> SaddleXRotation;
        public ConfigEntry<float> SaddleYRotation;
        public ConfigEntry<float> SaddleZRotation;
        public ConfigEntry<string> SaddlePrefab;
        public ConfigEntry<float> LegSpread;
        public ConfigEntry<float> CastSeconds;
        public ConfigEntry<int> RequiredRank;
        public ConfigEntry<string> RequiredBoss;
        public ConfigEntry<string> AllowedSteamIds;
        public ConfigEntry<float> FlySpeed;
        public ConfigEntry<float> FlyRunSpeed;

        /// <summary>Takes off with Space and flies (the Moder). Set in code: only a body with
        /// flying animations can do it.</summary>
        public bool Flies;

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

        /// <summary>Boss whose pass (NpcValheim's BossPass) the rider must hold, by its creature
        /// prefab ("Dragon"), or "" when there is none.</summary>
        public string Boss => RequiredBoss != null ? (RequiredBoss.Value ?? "").Trim() : "";
    }

    internal static class MountSettings
    {
        public static MountProfile Default { get; private set; }
        public static MountProfile[] All { get; private set; }

        public static ConfigEntry<float> RunSpeed => Default?.RunSpeed;
        public static ConfigEntry<float> WalkSpeed => Default?.WalkSpeed;
        public static ConfigEntry<float> JumpHeight => Default?.JumpHeight;
        public static ConfigEntry<float> Scale => Default?.Scale;
        public static ConfigEntry<float> SeatOffset => Default?.SeatOffset;
        public static ConfigEntry<float> CastSeconds => Default?.CastSeconds;
        public static ConfigEntry<bool> LockConfig;
        public static ConfigEntry<bool> UnlockAll;
        public static ConfigEntry<KeyboardShortcut> MenuKey;
        public static ConfigEntry<string> RidingRankList;
        public static ConfigEntry<bool> KnockOffOnHit;
        public static ConfigEntry<float> FlyMaxAltitude;
        public static ConfigEntry<float> FlyClimbSpeed;

        public static float Run => Default != null ? Default.RunSpeed.Value : 12f;
        public static float Walk => Default != null ? Default.WalkSpeed.Value : 5.5f;
        public static float Jump => Default != null ? Default.JumpHeight.Value : 8.5f;
        public static float Size => Default != null ? Mathf.Clamp(Default.Scale.Value, 0.5f, 3f) : 1f;
        public static float SeatHeightOffset => Default != null ? Mathf.Clamp(Default.SeatOffset.Value, -2f, 2f) : 0f;
        public static Vector3 SeatPositionOffset => Default != null
            ? new Vector3(Default.SeatXOffset.Value, 0f, Default.SeatZOffset.Value)
            : Vector3.zero;
        public static Vector3 SaddlePositionOffset => Default != null
            ? new Vector3(Default.SaddleXOffset.Value, Default.SaddleYOffset.Value, Default.SaddleZOffset.Value)
            : Vector3.zero;
        public static float SeatHeightOffsetFor(MountProfile profile) =>
            profile?.SeatOffset != null ? Mathf.Clamp(profile.SeatOffset.Value, -2f, 2f) : SeatHeightOffset;
        public static Vector3 SeatPositionOffsetFor(MountProfile profile) =>
            profile?.SeatXOffset != null && profile.SeatZOffset != null
                ? new Vector3(profile.SeatXOffset.Value, 0f, profile.SeatZOffset.Value)
                : SeatPositionOffset;
        public static Vector3 SaddlePositionOffsetFor(MountProfile profile) =>
            profile?.SaddleXOffset != null && profile.SaddleYOffset != null && profile.SaddleZOffset != null
                ? new Vector3(profile.SaddleXOffset.Value, profile.SaddleYOffset.Value, profile.SaddleZOffset.Value)
                : SaddlePositionOffset;
        public static Quaternion SaddleRotationFor(MountProfile profile) =>
            profile?.SaddleXRotation != null && profile.SaddleYRotation != null && profile.SaddleZRotation != null
                ? Quaternion.Euler(profile.SaddleXRotation.Value, profile.SaddleYRotation.Value, profile.SaddleZRotation.Value)
                : Quaternion.identity;
        public static float Cast => Default != null ? Mathf.Max(0.1f, Default.CastSeconds.Value) : 2f;

        public static void Init(BaseUnityPlugin plugin, ConfigSync sync)
        {
            var profiles = new List<MountProfile>();
            profiles.Add(CreateProfile("capivara", "Capivara", "CapivaraMontaria", "ApitoCapivara", "apito_capivara.png", "TrophyBoar"));
            profiles.Add(CreateProfile("neck", "Neck", "NeckMontaria", "ApitoNeck", null, "TrophyNeck"));
            profiles.Add(CreateProfile("deer", "Deer", "DeerMontaria", "ApitoDeer", null, "TrophyDeer"));
            profiles.Add(CreateProfile("boar", "Boar", "BoarMontaria", "ApitoBoar", null, "TrophyBoar"));
            profiles.Add(CreateProfile("eikthyr", "Eikthyr", "EikthyrMontaria", "ApitoEikthyr", null, "TrophyEikthyr"));
            var moder = CreateProfile("moder", "Moder", "ModerMontaria", "ApitoModer", null, "TrophyDragonQueen");
            moder.Flies = true;
            profiles.Add(moder);
            All = profiles.ToArray();
            Default = All.Length > 0 ? All[0] : null;

            foreach (var profile in All)
            {
                bool flies = profile.Flies;
                BindProfile(plugin, sync, profile, "Montaria" + profile.Id, 12f, 5.5f, 8.5f, 1f, 2f,
                    flies ? FlightRank : 1, flies ? "Dragon" : "");
            }

            KnockOffOnHit = Bind(plugin, sync, "Geral", "DerrubarAoTomarHit", true,
                "Qualquer golpe no cavaleiro ou na montaria (monstro, jogador, armadilha, torre...) derruba o cavaleiro, " +
                "e a montaria some. Queda, afogamento, fogo, veneno e frio contínuos não derrubam.");

            FlyMaxAltitude = Bind(plugin, sync, "Voo", "AlturaMaximaVoo", 40f,
                "Altura máxima de voo, em metros acima do chão (ou da água).");
            FlyClimbSpeed = Bind(plugin, sync, "Voo", "VelocidadeSubida", 1f,
                "Quanto Espaço/Ctrl sobem e descem em relação à velocidade de voo (1 = a mesma).");

            LockConfig = plugin.Config.Bind("Geral", "LockConfig", true,
                "Clientes usam a config do servidor.");
            sync.AddLockingConfigEntry(LockConfig);

            UnlockAll = Bind(plugin, sync, "Geral", "LiberarTodasGratis", false,
                "Se ligado, todo mundo tem a habilidade máxima e todas as montarias, sem comprar.");

            RidingRankList = Bind(plugin, sync, "Habilidade", "Niveis", RidingRanks.Default,
                "Níveis da Habilidade de Montaria, em ordem: nome:velocidade:cast separados por |. " +
                "A velocidade multiplica a da montaria de quem tem o nível (1 = sem bônus); " +
                "o cast multiplica o tempo de conjuração de todas as montarias (1 = sem redução, 0.5 = metade). " +
                "Quem vende é o Mestre das Montarias (NpcValheim).");
            if (Array.IndexOf(RidingRanks.OldDefaults, RidingRankList.Value) >= 0)
                RidingRankList.Value = RidingRanks.Default;
            RidingRankList.SettingChanged += (_, __) => MountHub.ApplySpeedAll();

            MenuKey = plugin.Config.Bind("Geral", "TeclaMenu", new KeyboardShortcut(KeyCode.U),
                "Abre o menu de montarias.");
        }

        /// <summary>The riding skill level that lets you fly: a flying mount asks for it by default.</summary>
        public const int FlightRank = 4;

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
                () => MountPrefab.ApplyProfile(profile));
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
                    "moder" => "moder",
                    _ => "capybara"
                };
            profile.PrefabName = prefabName;
            profile.ItemPrefab = itemPrefab;
            return profile;
        }

        private static void BindProfile(
            BaseUnityPlugin plugin, ConfigSync sync, MountProfile profile, string group,
            float run, float walk, float jump, float scale, float cast, int rank, string boss)
        {
            profile.CustomName = Bind(plugin, sync, group, "Nome", profile.DefaultName, "Nome exibido da montaria.");
            if (profile.CustomName.Value == "Default")
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
            profile.SaddleXRotation = Bind(plugin, sync, group, "SelaRotacaoX", 0f,
                "Rotação da sela no eixo X (inclina frente/trás), em graus.");
            profile.SaddleYRotation = Bind(plugin, sync, group, "SelaRotacaoY", 0f,
                "Rotação da sela no eixo Y (gira sobre as costas da montaria), em graus. Ex.: 90 ou 180.");
            profile.SaddleZRotation = Bind(plugin, sync, group, "SelaRotacaoZ", 0f,
                "Rotação da sela no eixo Z (inclina para os lados), em graus.");
            profile.SaddlePrefab = Bind(plugin, sync, group, "PrefabSela", "SaddleLox",
                "Prefab de item da sela usado na aparência da montaria (ex.: SaddleLox, SaddleAsksvin).");
            profile.LegSpread = Bind(plugin, sync, group, "AberturaPernas", 10f,
                "Abertura das pernas do personagem montado, em graus (0 desativa; máximo efetivo: 45).");
            profile.CastSeconds = Bind(plugin, sync, group, "CastSegundos", cast, "Tempo de conjuração para invocar.");
            profile.RequiredRank = Bind(plugin, sync, group, "HabilidadeMinima", rank,
                "Nível da Habilidade de Montaria exigido para invocar (1 = o primeiro nível).");
            profile.RequiredBoss = Bind(plugin, sync, group, "PasseExigido", boss,
                "Passe de chefe (BossPass do NpcValheim) exigido para invocar, pelo prefab do chefe como no " +
                "[BossPass] Bosses (gd_king, Bonemass, Dragon, GoblinKing, SeekerQueen, Fader). Vazio = nenhum. " +
                "Sem o NpcValheim vale ter ajudado a matar o chefe.");
            if (profile.Flies)
            {
                profile.FlySpeed = Bind(plugin, sync, group, "VelocidadeVoo", 10f, "Velocidade de voo.");
                profile.FlyRunSpeed = Bind(plugin, sync, group, "VelocidadeVooCorrida", 20f, "Velocidade de voo com Shift.");
                profile.FlySpeed.SettingChanged += (_, __) => profile.ApplyAll();
                profile.FlyRunSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            }
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
            profile.SaddleXRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleYRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleZRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddlePrefab.SettingChanged += (_, __) => profile.ApplyAll();
            profile.Scale.SettingChanged += (_, __) =>
            {
                profile.ApplyAll();
                if (Player.m_localPlayer != null)
                    MountPrefab.KeepHumanScale(Player.m_localPlayer);
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
