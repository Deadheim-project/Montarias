using System.IO;
using System.Linq;
using BepInEx;

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
        public const int Version = 1;

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
    }
}
