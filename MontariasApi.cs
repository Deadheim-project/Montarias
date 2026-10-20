using System.IO;
using System.Linq;
using BepInEx;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    /// <summary>
    /// What other mods may ask of Montarias. NpcValheim's Mestre das Montarias binds it by
    /// reflection (NpcValheim/Integration/MontariasApi.cs), so it uses primitive types only
    /// and neither mod needs the other to compile or load.
    ///
    /// Bump <see cref="Version"/> when a signature changes; the other side refuses a version
    /// it does not know rather than calling into the wrong shape.
    ///
    /// Server methods take the routed RPC sender of the request being served and resolve the
    /// account themselves, the same way the Deadcoins ledger does. They return null on success
    /// or the message to show the player.
    /// </summary>
    public static class MontariasApi
    {
        /// <summary>Version 2 added the "estábulo" section (LocalIsRiding/LocalRiddenMountId/
        /// LocalSummon/LocalDismount) for Deadheim's stable (Aesir 20): a fixed point where a
        /// player stores or calls back the mount registered to them. Everything else from
        /// version 1 is unchanged.</summary>
        public const int Version = 2;

        // ---- catalog (any side; from the server's synchronized config) ----

        public static string[] MountIds() =>
            MountSettings.All != null ? MountSettings.All.Select(p => p.Id).ToArray() : new string[0];

        public static string MountName(string id) => MountSettings.ById(id)?.Name ?? "";

        /// <summary>Absolute path of the mount's menu icon (a PNG), or "" when there is none.</summary>
        public static string MountIconPath(string id)
        {
            var file = MountSettings.ById(id)?.IconFile;
            if (string.IsNullOrEmpty(file)) return "";
            var path = Path.Combine(Path.Combine(Path.Combine(Paths.PluginPath, "ValheimMontarias"), "Assets"), file);
            return File.Exists(path) ? path : "";
        }

        /// <summary>Riding skill level the mount needs to be summoned.</summary>
        public static int MountRequiredRank(string id) => MountSettings.ById(id)?.MinRank ?? 1;

        public static int RankCount() => RidingRanks.Count;

        /// <summary>Name of a level, 1-based.</summary>
        public static string RankName(int rank) => RidingRanks.NameOf(rank);

        public static float RankSpeed(int rank) => RidingRanks.SpeedOf(rank);

        /// <summary>What a level multiplies every cast time by (1 = no change, 0.5 = half).</summary>
        public static float RankCast(int rank) => RidingRanks.CastOf(rank);

        // ---- local player (client) ----

        /// <summary>Whether the server has answered yet; until then Rank/Owns read as nothing.</summary>
        public static bool LocalKnown() => RiderClient.Known;

        public static int LocalRank() => RiderClient.Rank;

        public static bool LocalOwns(string id) => RiderClient.Owns(id);

        /// <summary>Goes up whenever the local state changes, for a view that redraws on change.</summary>
        public static int LocalRevision() => RiderClient.Revision;

        public static void RequestLocalState() => RiderClient.Request(true);

        // ---- server ----

        /// <summary>The sender's level, or -1 when the server cannot tell whose it is.</summary>
        public static int ServerRank(long sender) => RiderServer.RankOf(sender);

        public static bool ServerOwns(long sender, string mountId) => RiderServer.Owns(sender, mountId);

        public static string ServerGrantRank(long sender, int rank, string why) =>
            RiderServer.GrantRank(sender, rank, string.IsNullOrEmpty(why) ? "outro mod" : why);

        public static string ServerGrantMount(long sender, string mountId, string why) =>
            RiderServer.GrantMount(sender, mountId, string.IsNullOrEmpty(why) ? "outro mod" : why);

        // ---- estábulo (Deadheim, Aesir 20) ----
        //
        // Guardar/chamar a montaria de um ponto fixo não precisa de protocolo novo: é só
        // disparar o mesmo invocar/desmontar que o item e o H já fazem, a partir de onde quer
        // que o Deadheim decida (um gatilho de proximidade, uma interação no estábulo...).
        // A posse, a habilidade e o passe de chefe continuam sendo os de sempre (LocalOwns,
        // ServerOwns, MountRequiredRank) -- o estábulo não dá nada que o jogador não tivesse.

        /// <summary>Se o jogador local está montado em uma das nossas montarias agora.</summary>
        public static bool LocalIsRiding() => MountControl.IsRiding(Player.m_localPlayer);

        /// <summary>O id da montaria que o jogador local está usando agora, ou "" se não
        /// estiver montado (para o estábulo saber o que guardar).</summary>
        public static string LocalRiddenMountId()
        {
            if (!MountControl.TryGetRidden(Player.m_localPlayer, out var mount, out _, out _) || mount == null)
                return "";
            return MountSettings.Find(mount.gameObject)?.Id ?? "";
        }

        /// <summary>Invoca a montaria (mesmo fluxo do item/H: conjuração, checagens de posse/
        /// habilidade/passe, mensagem ao jogador se algo faltar). "" ou um id desconhecido usa
        /// a montaria padrão. Sempre true (o pedido foi aceito); o resultado chega pela
        /// mensagem central do jogo, como qualquer invocação.</summary>
        public static bool LocalSummon(string mountId)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            var profile = MountSettings.ById(mountId) ?? MountSettings.Default;
            if (profile == null) return false;
            MountControl.TrySummonProfile(player, profile);
            return true;
        }

        /// <summary>Desmonta (se estiver montado) e a montaria some, como o item/H fazem.</summary>
        public static bool LocalDismount()
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            MountControl.Dismount(player);
            return true;
        }
    }
}
