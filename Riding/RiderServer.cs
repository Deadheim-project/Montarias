using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;

namespace ValheimMontarias
{
    /// <summary>
    /// What each rider knows and owns, kept on the server: one file per character,
    /// BepInEx/config/ValheimMontarias/cavaleiros/&lt;name&gt;-&lt;account&gt;.txt, holding
    /// "rank=N" and "mounts=id,id". Same key as the Deadcoins balance files of NpcValheim, so
    /// an admin finds a player's mounts next to the name they already know.
    ///
    /// Only the server reads or writes it, and it decides whose file it is from the connection
    /// itself. The client is told its own state and nothing else; a client cannot name an
    /// account, a level or a mount it was not granted.
    ///
    /// Purchases come in from NpcValheim through <see cref="MontariasApi"/>; admins grant and
    /// reset through <see cref="RiderNet"/>.
    /// </summary>
    internal static class RiderServer
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal sealed class Record
        {
            public int Rank;
            public readonly HashSet<string> Mounts = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Aesir 28: nome escolhido pelo jogador, por montaria (mountId -> nome).</summary>
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Aesir 30: skin escolhida pelo jogador, por montaria (mountId -> skinId).</summary>
            public readonly Dictionary<string, string> Skins = new Dictionary<string, string>(StringComparer.Ordinal);

            public string MountList => string.Join(",", Mounts.OrderBy(m => m, StringComparer.Ordinal));
        }

        internal static string Folder => Path.Combine(Path.Combine(Paths.ConfigPath, "ValheimMontarias"), "cavaleiros");

        private static string LogPath => Path.Combine(Path.Combine(Paths.ConfigPath, "ValheimMontarias"), "cavaleiros.log");

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ---- identity ----

        /// <summary>The file key of the character behind a routed RPC sender:
        /// "&lt;name&gt;-&lt;account&gt;", with the account written the way NpcValheim's
        /// Deadcoins ledger writes it ("Steam_7656...").</summary>
        internal static bool TryResolve(long sender, out string key, out string who)
        {
            key = null;
            who = $"peer {sender}";
            if (!IsServer) return false;

            string name = null;
            var peer = FindPeer(sender);
            if (peer != null)
                name = Access.Get(peer, "m_playerName") as string;
            else if (IsLocalSender(sender))
            {
                name = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
                if (string.IsNullOrWhiteSpace(name))
                    name = Game.instance != null ? Game.instance.GetPlayerProfile()?.GetName() : null;
            }

            string account = AccountIdOf(sender);
            who = $"{name ?? "???"} ({(account.Length > 0 ? account : "sem conta")})";
            if (string.IsNullOrWhiteSpace(name) || account.Length == 0) return false;

            var file = name.Trim() + "-" + account;
            if (file.IndexOf('/') >= 0 || file.IndexOf('\\') >= 0 ||
                file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            key = file;
            return true;
        }

        /// <summary>The account id ("Steam_765...") behind a routed RPC sender, the same way
        /// <see cref="TryResolve"/> derives it -- used by <see cref="Integration.VipProvider"/>
        /// to look the sender up on the VIP list without needing their character name.</summary>
        internal static string AccountIdOf(long sender)
        {
            if (!IsServer) return "";
            string host = null;
            var peer = FindPeer(sender);
            if (peer != null) host = HostName(peer);
            else if (IsLocalSender(sender)) host = LocalPlatformId();
            return CanonicalAccountId(host);
        }

        /// <summary>A Steam socket reports the bare SteamID, a crossplay one "Platform_id";
        /// both end up as "Steam_7656..." / "Xbox_..." like the Deadcoins files.</summary>
        internal static string CanonicalAccountId(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName)) return "";
            hostName = hostName.Trim();
            return hostName.IndexOf('_') > 0 ? hostName : "Steam_" + hostName;
        }

        /// <summary>Whether a routed RPC sender is this very process (host or solo play).</summary>
        internal static bool IsLocalSender(long sender)
        {
            if (sender == 0L || !IsServer) return false;
            if (ZRoutedRpc.instance != null && Access.Get(ZRoutedRpc.instance, "m_id") is long id && id == sender)
                return true;
            return sender == ZNet.GetUID();
        }

        /// <summary>Whether the peer behind a sender is on the server's admin list. Fails closed:
        /// no peer and not this process means no.</summary>
        internal static bool IsAdmin(long sender)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null) return false;
                var peer = FindPeer(sender);
                if (peer == null)
                    return IsLocalSender(sender) && Access.IsAdmin();

                string host = HostName(peer);
                if (string.IsNullOrEmpty(host)) return false;
                var list = Access.Get(znet, "m_adminList");
                if (list == null) return false;

                var contains = typeof(ZNet).GetMethod("ListContainsId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (contains != null)
                    return (bool)contains.Invoke(contains.IsStatic ? null : znet, new[] { list, host });

                var fallback = list.GetType().GetMethod("Contains", AnyInstance, null, new[] { typeof(string) }, null);
                return fallback != null && (bool)fallback.Invoke(list, new object[] { host });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: admin check failed for {sender}: {e.Message}");
                return false;
            }
        }

        private static ZNetPeer FindPeer(long sender)
        {
            if (sender == 0L || ZNet.instance == null) return null;
            var getPeer = typeof(ZNet).GetMethod("GetPeer", AnyInstance, null, new[] { typeof(long) }, null);
            return getPeer != null ? getPeer.Invoke(ZNet.instance, new object[] { sender }) as ZNetPeer : null;
        }

        private static string HostName(ZNetPeer peer)
        {
            var socket = Access.Get(peer, "m_socket");
            if (socket == null)
            {
                var rpc = Access.Get(peer, "m_rpc");
                socket = rpc != null ? Access.Call(rpc, "GetSocket") : null;
            }
            return socket != null ? Access.Call(socket, "GetHostName") as string : null;
        }

        /// <summary>PlayFabManager.m_customId, read by reflection: in Valheim 1.0 it is a
        /// Splatform.PlatformUserID, and naming that type would add a reference for one line.</summary>
        private static string LocalPlatformId()
        {
            var field = typeof(PlayFabManager).GetField("m_customId",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(null)?.ToString();
        }

        // ---- ledger ----

        /// <summary>Reads a record. No file is a rider who knows nothing yet (rank 0, no
        /// mounts). A file that cannot be read is an error, so that a grant never overwrites
        /// what an admin was in the middle of writing.</summary>
        internal static bool TryLoad(string key, out Record record)
        {
            record = new Record();
            var path = PathOf(key);
            if (!File.Exists(path)) return true;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    var name = line.Substring(0, equals).Trim();
                    var value = line.Substring(equals + 1).Trim();
                    if (name.Equals("rank", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out record.Rank))
                            return false;
                    }
                    else if (name.Equals("mounts", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var id in value.Split(','))
                            if (id.Trim().Length > 0) record.Mounts.Add(id.Trim());
                    }
                    else if (name.StartsWith("name_", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = name.Substring(5).Trim();
                        if (id.Length > 0 && value.Length > 0) record.Names[id] = value;
                    }
                    else if (name.StartsWith("skin_", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = name.Substring(5).Trim();
                        if (id.Length > 0 && value.Length > 0) record.Skins[id] = value;
                    }
                }
                record.Rank = Math.Max(0, record.Rank);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ValheimMontarias: could not read {path}: {e.Message}");
                return false;
            }
        }

        internal static void Save(string key, Record record)
        {
            Directory.CreateDirectory(Folder);
            var text = new StringBuilder();
            text.Append("rank=").Append(record.Rank.ToString(CultureInfo.InvariantCulture)).AppendLine();
            text.Append("mounts=").Append(record.MountList).AppendLine();
            foreach (var pair in record.Names.OrderBy(p => p.Key, StringComparer.Ordinal))
                text.Append("name_").Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            foreach (var pair in record.Skins.OrderBy(p => p.Key, StringComparer.Ordinal))
                text.Append("skin_").Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            File.WriteAllText(PathOf(key), text.ToString());
        }

        /// <summary>Strips newlines, the net-protocol separators (":" ";") and control
        /// characters from a player-chosen display string (a mount's name), then clamps it.
        /// Empty input stays empty (clears the custom name).</summary>
        internal static string SanitizeDisplayName(string raw, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var text = new StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
            {
                if (ch == ':' || ch == ';' || char.IsControl(ch)) continue;
                text.Append(ch);
            }
            var result = text.ToString().Trim();
            maxLength = Math.Max(1, maxLength);
            if (result.Length > maxLength) result = result.Substring(0, maxLength).Trim();
            return result;
        }

        internal static void AppendLog(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: rider log not written: {e.Message}");
            }
        }

        private static string PathOf(string key) => Path.Combine(Folder, key + ".txt");

        // ---- operations (server only) ----

        /// <summary>The sender's level, or -1 when the server cannot tell whose file it is.</summary>
        internal static int RankOf(long sender)
        {
            if (!TryResolve(sender, out var key, out _) || !TryLoad(key, out var record)) return -1;
            return record.Rank;
        }

        internal static bool Owns(long sender, string mountId)
        {
            return TryResolve(sender, out var key, out _) && TryLoad(key, out var record) &&
                   record.Mounts.Contains(mountId ?? "");
        }

        /// <summary>Teaches a level. Null on success, else the reason for the player. `why`
        /// goes to the log ("compra no Mestre das Montarias", "admin").</summary>
        internal static string GrantRank(long sender, int rank, string why)
        {
            if (rank < 1 || rank > RidingRanks.Count) return "Este nível de habilidade não existe.";
            return Change(sender, why, record =>
            {
                if (record.Rank >= rank) return $"Você já sabe {RidingRanks.NameOf(record.Rank)}.";
                record.Rank = rank;
                return null;
            }, $"aprendeu {RidingRanks.NameOf(rank)} (nível {rank})");
        }

        internal static string GrantMount(long sender, string mountId, string why)
        {
            var profile = MountSettings.ById(mountId);
            if (profile == null) return "Esta montaria não existe.";
            return Change(sender, why, record =>
            {
                if (!record.Mounts.Add(profile.Id)) return $"Você já possui {profile.Name}.";
                return null;
            }, $"ganhou a montaria {profile.Id}");
        }

        /// <summary>Aesir 28: nomeia (ou, com nome vazio, limpa o nome de) uma montaria que o
        /// jogador possui. A posse e o VIP (quando exigido pela config) já foram conferidos
        /// pelo chamador; aqui a posse é conferida de novo, pois é o servidor quem decide.</summary>
        internal static string SetName(long sender, string mountId, string name, string why)
        {
            var profile = MountSettings.ById(mountId);
            if (profile == null) return "Esta montaria não existe.";
            string what = string.IsNullOrEmpty(name)
                ? $"removeu o nome de {profile.Id}"
                : $"nomeou {profile.Id} como \"{name}\"";
            return Change(sender, why, record =>
            {
                if (!record.Mounts.Contains(profile.Id)) return $"Você não possui {profile.Name}.";
                if (string.IsNullOrEmpty(name)) record.Names.Remove(profile.Id);
                else record.Names[profile.Id] = name;
                return null;
            }, what);
        }

        /// <summary>Aesir 30: escolhe (ou, com skinId vazio/"Padrao", limpa) a skin de uma
        /// montaria que o jogador possui.</summary>
        internal static string SetSkin(long sender, string mountId, string skinId, string why)
        {
            var profile = MountSettings.ById(mountId);
            if (profile == null) return "Esta montaria não existe.";
            bool clear = string.IsNullOrEmpty(skinId) ||
                         skinId.Equals(MountProfile.DefaultSkinId, StringComparison.OrdinalIgnoreCase);
            string what = clear ? $"removeu a skin de {profile.Id}" : $"escolheu a skin \"{skinId}\" de {profile.Id}";
            return Change(sender, why, record =>
            {
                if (!record.Mounts.Contains(profile.Id)) return $"Você não possui {profile.Name}.";
                if (clear)
                {
                    record.Skins.Remove(profile.Id);
                    return null;
                }
                if (!profile.TryGetSkinColor(skinId, out _)) return "Esta skin não existe.";
                record.Skins[profile.Id] = skinId;
                return null;
            }, what);
        }

        internal static string Reset(long sender, string why)
        {
            return Change(sender, why, record =>
            {
                record.Rank = 0;
                record.Mounts.Clear();
                record.Names.Clear();
                record.Skins.Clear();
                return null;
            }, "zerou habilidade e montarias");
        }

        private static string Change(long sender, string why, Func<Record, string> edit, string what)
        {
            if (!IsServer) return "Só o servidor pode fazer isso.";
            if (!TryResolve(sender, out var key, out var who))
            {
                Plugin.Log.LogWarning($"ValheimMontarias: no rider account for {who}");
                return "O servidor não conseguiu identificar a sua conta.";
            }
            if (!TryLoad(key, out var record))
                return "Sua ficha de montarias está ilegível no servidor. Fale com um admin.";

            var refusal = edit(record);
            if (refusal != null) return refusal;

            try
            {
                Save(key, record);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ValheimMontarias: could not save the rider file of {who}: {e.Message}");
                return "Falha ao gravar no servidor. Nada mudou.";
            }

            Plugin.Log.LogInfo($"ValheimMontarias: {who} {what} ({why})");
            AppendLog($"{who} {what} ({why}) -> rank={record.Rank} mounts={record.MountList}");
            RiderNet.SendState(sender, record, null);
            return null;
        }
    }
}
