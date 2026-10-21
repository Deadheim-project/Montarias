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
        /// <summary>Fine adjustments, in metres, on top of the automatic fit (saddle on the
        /// posed back, rider's hips on the seat, knees along the flanks). All 0 by default.</summary>
        public ConfigEntry<float> SaddleUp;
        public ConfigEntry<float> SaddleForward;
        public ConfigEntry<float> SeatBack;
        public ConfigEntry<float> SaddleSize;
        public ConfigEntry<float> RiderUp;
        public ConfigEntry<float> RiderForward;
        public ConfigEntry<float> LegGap;
        /// <summary>The riding pose made in code (RiderPose) and its numbers.</summary>
        public ConfigEntry<bool> CodedPose;
        public ConfigEntry<float> KneeForward;
        public ConfigEntry<float> ThighOut;
        public ConfigEntry<float> ShinBack;
        public ConfigEntry<float> Lean;
        public ConfigEntry<float> HandsForward;
        public ConfigEntry<float> HandsUp;
        public ConfigEntry<float> SaddleXRotation;
        public ConfigEntry<float> SaddleYRotation;
        public ConfigEntry<float> SaddleZRotation;
        public ConfigEntry<string> SaddlePrefab;
        public ConfigEntry<string> RidePose;
        public ConfigEntry<float> CastSeconds;
        public ConfigEntry<int> RequiredRank;
        public ConfigEntry<string> RequiredBoss;
        public ConfigEntry<string> AllowedSteamIds;
        public ConfigEntry<float> FlySpeed;
        public ConfigEntry<float> FlyRunSpeed;

        /// <summary>Takes off with Space and flies (the Moder). Set in code: only a body with
        /// flying animations can do it.</summary>
        public bool Flies;

        /// <summary>Aesir 30: cosmetic coat tints the owner may pick for this mount (same
        /// stats). "Id:#RRGGBB" entries separated by "|"; "Padrao" (no tint) always exists
        /// and does not need to be listed.</summary>
        public ConfigEntry<string> SkinList;

        /// <summary>Every skin option for this mount, "Padrao" (no tint, white) always first.</summary>
        public List<(string Id, Color Color)> SkinOptions()
        {
            var list = new List<(string, Color)> { (DefaultSkinId, Color.white) };
            if (SkinList == null || string.IsNullOrWhiteSpace(SkinList.Value))
                return list;

            foreach (var entry in SkinList.Value.Split('|'))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0) continue;
                int sep = trimmed.IndexOf(':');
                if (sep <= 0) continue;
                var id = trimmed.Substring(0, sep).Trim();
                var colorText = trimmed.Substring(sep + 1).Trim();
                if (id.Length == 0 || id.Equals(DefaultSkinId, StringComparison.OrdinalIgnoreCase)) continue;
                if (ColorUtility.TryParseHtmlString(colorText, out var color))
                    list.Add((id, color));
            }
            return list;
        }

        public const string DefaultSkinId = "Padrao";

        /// <summary>The tint of a skin id, or white (no tint) when it does not exist.</summary>
        public bool TryGetSkinColor(string id, out Color color)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (var option in SkinOptions())
                {
                    if (string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        color = option.Color;
                        return true;
                    }
                }
            }
            color = Color.white;
            return false;
        }

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
        public static ConfigEntry<float> CastSeconds => Default?.CastSeconds;
        public static ConfigEntry<bool> LockConfig;
        public static ConfigEntry<bool> UnlockAll;
        public static ConfigEntry<KeyboardShortcut> MenuKey;
        public static ConfigEntry<string> RidingRankList;
        public static ConfigEntry<bool> KnockOffOnHit;
        public static ConfigEntry<float> FlyMaxAltitude;
        public static ConfigEntry<float> FlyClimbSpeed;

        /// <summary>Aesir 28: cada jogador escolhe um nome para a montaria que possui,
        /// visível para todos acima dela.</summary>
        public static ConfigEntry<bool> MountNameActive;
        public static ConfigEntry<bool> MountNameVipOnly;
        public static ConfigEntry<int> MountNameMaxLength;

        /// <summary>Aesir 30: cada jogador escolhe uma skin (tingimento, mesmos stats) para a
        /// montaria que possui.</summary>
        public static ConfigEntry<bool> MountSkinActive;
        public static ConfigEntry<bool> MountSkinVipOnly;

        public static float Run => Default != null ? Default.RunSpeed.Value : 12f;
        public static float Walk => Default != null ? Default.WalkSpeed.Value : 5.5f;
        public static float Jump => Default != null ? Default.JumpHeight.Value : 8.5f;
        public static float Size => Default != null ? Mathf.Clamp(Default.Scale.Value, 0.5f, 3f) : 1f;
        /// <summary>The saddle's adjustment on top of the fit, in metres in the mount's space
        /// (y up, z forward).</summary>
        public static Vector3 SaddleAdjustFor(MountProfile profile) => new Vector3(0f,
            Metres(profile?.SaddleUp), Metres(profile?.SaddleForward));
        /// <summary>How far behind the middle of the body the saddle is fitted (metres).</summary>
        public static float SeatBackFor(MountProfile profile) =>
            profile?.SeatBack != null ? Mathf.Clamp(profile.SeatBack.Value, -1.5f, 1.5f) : 0f;
        /// <summary>The saddle's size against the one the fit gives it (as long as the back is wide).</summary>
        public static float SaddleSizeFor(MountProfile profile) =>
            profile?.SaddleSize != null ? Mathf.Clamp(profile.SaddleSize.Value, 0.3f, 3f) : 1f;
        /// <summary>The rider's hips adjustment on top of the fit, likewise.</summary>
        public static Vector3 RiderAdjustFor(MountProfile profile) => new Vector3(0f,
            Metres(profile?.RiderUp), Metres(profile?.RiderForward));
        /// <summary>Room between the rider's knees and the mount's flanks.</summary>
        public static float LegGapFor(MountProfile profile) =>
            profile?.LegGap != null ? Mathf.Clamp(profile.LegGap.Value, -0.2f, 0.5f) : 0.07f;
        public static bool CodedPoseFor(MountProfile profile) => profile?.CodedPose == null || profile.CodedPose.Value;

        /// <summary>The mount's riding pose numbers into <paramref name="settings"/>.</summary>
        public static void FillPose(MountProfile profile, RiderPose.Settings settings)
        {
            settings.KneeForward = profile.KneeForward != null ? Mathf.Clamp(profile.KneeForward.Value, -30f, 90f) : 35f;
            settings.ThighOut = profile.ThighOut != null ? Mathf.Clamp(profile.ThighOut.Value, 0f, 85f) : 40f;
            settings.ShinBack = profile.ShinBack != null ? Mathf.Clamp(profile.ShinBack.Value, -60f, 60f) : 30f;
            settings.Lean = profile.Lean != null ? Mathf.Clamp(profile.Lean.Value, -30f, 60f) : 20f;
            settings.Gap = LegGapFor(profile);
            settings.Hips = new Vector3(0f, MountGear.HipsOverSeat, 0f) + RiderAdjustFor(profile);
            float forward = profile.HandsForward != null ? Mathf.Clamp(profile.HandsForward.Value, -1f, 1.5f) : 0.65f;
            float up = profile.HandsUp != null ? Mathf.Clamp(profile.HandsUp.Value, -1f, 1.5f) : 0.35f;
            settings.Hands = forward == 0f && up == 0f ? Vector3.zero : new Vector3(0.17f, up, forward);
        }

        private static float Metres(ConfigEntry<float> entry) =>
            entry != null ? Mathf.Clamp(entry.Value, -2f, 2f) : 0f;
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
                    flies ? FlightRank : 1, flies ? "Dragon" : "", DefaultRidePose(profile.Id));
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

            MountNameActive = Bind(plugin, sync, "Cosmeticos", "NomeMontariaAtivo", true,
                "Aesir 28: deixa o jogador dar um nome à montaria que possui (visível para todos acima dela).");
            MountNameVipOnly = Bind(plugin, sync, "Cosmeticos", "NomeMontariaSoVip", true,
                "Só VIP (Aesir) pode escolher o nome da montaria.");
            MountNameMaxLength = Bind(plugin, sync, "Cosmeticos", "NomeMontariaTamanho", 20,
                "Tamanho máximo do nome escolhido pelo jogador.");

            MountSkinActive = Bind(plugin, sync, "Cosmeticos", "SkinMontariaAtivo", true,
                "Aesir 30: deixa o jogador escolher uma skin (tingimento, mesmos stats) para a montaria que possui.");
            MountSkinVipOnly = Bind(plugin, sync, "Cosmeticos", "SkinMontariaSoVip", true,
                "Só VIP (Aesir) pode escolher a skin da montaria.");

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

        /// <summary>Rider poses <see cref="MountGear.RideAnimation"/> knows by name: the riding
        /// poses of the vanilla Lox and Asksvin saddles, and the seated one ("Cadeira").</summary>
        public static readonly string[] RidePoses = { "Lox", "Asksvin", "Cadeira" };

        /// <summary>The Lox pose (upright, legs wide apart) for the wide bodies, the Asksvin one
        /// (leaning forward, legs closer) for the slim ones.</summary>
        private static string DefaultRidePose(string id) =>
            id == "deer" || id == "eikthyr" || id == "neck" ? "Asksvin" : "Lox";

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
            // A mount keeps its profile: matching by name reads go.name, a new string per
            // profile tried, and this is asked several times a frame for every rider.
            if (go.TryGetComponent(out MountControl mount)) return mount.Profile;
            return FindByName(go);
        }

        internal static MountProfile FindByName(GameObject go)
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
            float run, float walk, float jump, float scale, float cast, int rank, string boss, string ridePose)
        {
            profile.CustomName = Bind(plugin, sync, group, "Nome", profile.DefaultName, "Nome exibido da montaria.");
            if (profile.CustomName.Value == "Default")
                profile.CustomName.Value = profile.DefaultName;
            profile.RunSpeed = Bind(plugin, sync, group, "VelocidadeCorrida", run, "Velocidade de corrida da montaria.");
            profile.WalkSpeed = Bind(plugin, sync, group, "VelocidadeAndar", walk, "Velocidade de andar da montaria.");
            profile.JumpHeight = Bind(plugin, sync, group, "AlturaPulo", jump, "Impulso vertical do pulo da montaria.");
            profile.Scale = Bind(plugin, sync, group, "Escala", scale, "Escala da montaria (1 = tamanho vanilla).");
            // The saddle, the rider and the legs fit themselves to the posed body; these only
            // nudge the result. The old AlturaAssento/AssentoOffset*/SelaOffset*/AberturaPernas
            // were tuned against placements that no longer exist and are not read any more.
            profile.SaddleUp = Bind(plugin, sync, group, "AjusteSelaAltura", 0f,
                "Sobe (+) ou desce (-) a sela, em metros, depois do encaixe automático nas costas.");
            profile.SaddleForward = Bind(plugin, sync, group, "AjusteSelaFrente", 0f,
                "Leva a sela para a frente (+) ou para trás (-), em metros.");
            profile.SeatBack = Bind(plugin, sync, group, "AssentoRecuo", profile.Id == "neck" ? 0.45f : 0f,
                "Encaixa a sela e o cavaleiro mais para trás (+) ou para a frente (-) no lombo, em metros, " +
                "medindo de novo a altura das costas ali (o Neck senta perto do rabo).");
            profile.SaddleSize = Bind(plugin, sync, group, "AjusteSelaTamanho", 1f,
                "Multiplica o tamanho da sela (1 = o automático, do comprimento da largura das costas).");
            profile.RiderUp = Bind(plugin, sync, group, "AjustePersonagemAltura", 0f,
                "Sobe (+) ou desce (-) o personagem, em metros, depois do encaixe automático do quadril no assento.");
            profile.RiderForward = Bind(plugin, sync, group, "AjustePersonagemFrente", 0f,
                "Leva o personagem para a frente (+) ou para trás (-) na sela, em metros.");
            profile.LegGap = Bind(plugin, sync, group, "FolgaPernas", 0.07f,
                "Distância entre os joelhos do personagem e os flancos da montaria, em metros " +
                "(as pernas se abrem ou fecham sozinhas até ela).");
            profile.CodedPose = Bind(plugin, sync, group, "PoseCodigo", true,
                "Monta a pose do cavaleiro por código em cima da PoseMontado: quadril na sela, tronco inclinado, " +
                "pernas em volta do corpo da montaria e mãos à frente. Desligado, fica só a animação do jogo.");
            profile.KneeForward = Bind(plugin, sync, group, "PoseJoelhoFrente", 35f,
                "Pose por código: quanto a coxa aponta para a frente, em graus (0 = reta para baixo, 90 = horizontal).");
            profile.ThighOut = Bind(plugin, sync, group, "PoseCoxasAbertas", 40f,
                "Pose por código: quanto as coxas se abrem para os lados, em graus (0 = juntas; abrem mais sozinhas se o corpo da montaria pedir).");
            profile.ShinBack = Bind(plugin, sync, group, "PoseCanelaTras", 30f,
                "Pose por código: quanto a canela vai para trás da vertical, em graus.");
            profile.Lean = Bind(plugin, sync, group, "PoseInclinacaoTronco", 20f,
                "Pose por código: quanto o tronco inclina para a frente, em graus.");
            profile.HandsForward = Bind(plugin, sync, group, "PoseMaosFrente", 0.65f,
                "Pose por código: as mãos à frente do assento, em metros (com PoseMaosAltura 0, as duas 0 deixam os braços da animação).");
            profile.HandsUp = Bind(plugin, sync, group, "PoseMaosAltura", 0.35f,
                "Pose por código: as mãos acima do assento, em metros. Só vale onde não há corpo da montaria à frente; " +
                "onde há, as mãos seguram em cima dele (na crina/pescoço).");
            profile.SaddleXRotation = Bind(plugin, sync, group, "SelaRotacaoX", 0f,
                "Rotação da sela no eixo X (inclina frente/trás), em graus.");
            profile.SaddleYRotation = Bind(plugin, sync, group, "SelaRotacaoY", 0f,
                "Rotação da sela no eixo Y (gira sobre as costas da montaria), em graus. Ex.: 90 ou 180.");
            profile.SaddleZRotation = Bind(plugin, sync, group, "SelaRotacaoZ", 0f,
                "Rotação da sela no eixo Z (inclina para os lados), em graus.");
            profile.SaddlePrefab = Bind(plugin, sync, group, "PrefabSela", "SaddleLox",
                "Prefab de item da sela usado na aparência da montaria (ex.: SaddleLox, SaddleAsksvin).");
            profile.RidePose = Bind(plugin, sync, group, "PoseMontado", ridePose,
                "Pose do personagem montado: Lox (ereto, pernas bem abertas), Asksvin (inclinado para a frente, " +
                "pernas mais fechadas) -- as poses de montar das selas desses bichos do jogo --, ou Cadeira " +
                "(sentado com as mãos nos joelhos, a pose antiga). Aceita também o nome de uma animação attach_ do jogador.");
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

            profile.SkinList = Bind(plugin, sync, group, "Skins", "Escura:#3A2C22|Clara:#E8DDCF|Dourada:#D4AF37",
                "Skins cosméticas desta montaria (mesmos stats, Aesir 30): \"Nome:#RRGGBB\" separados por \"|\". " +
                "\"Padrao\" (sem tingimento) sempre existe e não precisa estar na lista.");

            profile.CustomName.SettingChanged += (_, __) => profile.ApplyAll();
            profile.RunSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.WalkSpeed.SettingChanged += (_, __) => profile.ApplyAll();
            profile.JumpHeight.SettingChanged += (_, __) => profile.ApplyAll();
            // Refit, not rebuild: the saddle moves from where it is, without a frame at the
            // unfitted spot under a rider who is tuning it.
            profile.SaddleUp.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.SaddleForward.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.SeatBack.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.SaddleSize.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.RiderUp.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.RiderForward.SettingChanged += (_, __) => MountPrefab.RefitProfile(profile);
            profile.SaddleXRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleYRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddleZRotation.SettingChanged += (_, __) => profile.ApplyAll();
            profile.SaddlePrefab.SettingChanged += (_, __) => profile.ApplyAll();
            profile.RidePose.SettingChanged += (_, __) => profile.ApplyAll();
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
