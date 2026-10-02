using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// The two routed RPCs between a rider and the server.
    ///
    /// Request (client -> server): an action and one argument. "state" asks for your own level
    /// and mounts; the admin-* actions edit your own record and are refused unless the
    /// server's admin list has you. There is no way to name another player: the server
    /// resolves whose record it is from the connection.
    ///
    /// State (server -> client): "rank\nmount,mount" plus an optional message. Sent as the
    /// answer to a request and pushed on its own after any change, so a purchase made at
    /// NpcValheim's Mestre das Montarias shows up in the mount menu without asking.
    /// </summary>
    internal static class RiderNet
    {
        private const string RpcRequest = "ValheimMontarias_RiderRequest";
        private const string RpcState = "ValheimMontarias_RiderState";

        internal const string ActionState = "state";
        internal const string ActionGrantMount = "admin-grant-mount";
        internal const string ActionNextRank = "admin-next-rank";
        internal const string ActionReset = "admin-reset";

        private static ZRoutedRpc _registered;
        private static readonly Dictionary<long, (float start, int count)> Rate =
            new Dictionary<long, (float, int)>();

        /// <summary>Once per connection: every ZNet.Start makes a new ZRoutedRpc.</summary>
        internal static void TryRegister()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registered)) return;
            _registered = rpc;
            Rate.Clear();
            RiderClient.Reset();
            rpc.Register(RpcRequest, (Action<long, string, string>)RPC_Request);
            rpc.Register(RpcState, (Action<long, string, string>)RPC_State);
        }

        // ---- client -> server ----

        internal static bool Send(string action, string argument)
        {
            TryRegister();
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return false;
            long server = ServerPeerId();
            if (server == 0L) return false;
            rpc.InvokeRoutedRPC(server, RpcRequest, new object[] { action ?? "", argument ?? "" });
            return true;
        }

        private static long ServerPeerId()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return 0L;
            if (RiderServer.IsServer && Access.Get(rpc, "m_id") is long self) return self;
            return Access.Call(rpc, "GetServerPeerID") is long id ? id : 0L;
        }

        private static bool FromServer(long sender)
        {
            if (ZNet.instance == null) return false;
            if (ZNet.instance.IsServer()) return RiderServer.IsLocalSender(sender);
            return sender == ServerPeerId();
        }

        // ---- server ----

        private static void RPC_Request(long sender, string action, string argument)
        {
            if (!RiderServer.IsServer) return;
            if (!AllowRate(sender)) return;

            if (!RiderServer.TryResolve(sender, out var key, out var who))
            {
                Plugin.Log.LogWarning($"ValheimMontarias: '{action}' from {who}, account not resolved");
                SendMessage(sender, "O servidor não conseguiu identificar a sua conta.");
                return;
            }

            if (action == ActionState)
            {
                if (RiderServer.TryLoad(key, out var record)) SendState(sender, record, null);
                else SendMessage(sender, "Sua ficha de montarias está ilegível no servidor. Fale com um admin.");
                return;
            }

            if (!RiderServer.IsAdmin(sender))
            {
                Plugin.Log.LogWarning($"ValheimMontarias: refused admin action '{action}' from {who}");
                SendMessage(sender, "Apenas administradores.");
                return;
            }

            string refusal;
            string done;
            switch (action)
            {
                case ActionGrantMount:
                    refusal = RiderServer.GrantMount(sender, argument, "admin");
                    var profile = MountSettings.ById(argument);
                    done = $"{(profile != null ? profile.Name : argument)} liberada para você.";
                    break;
                case ActionNextRank:
                    int next = RiderServer.RankOf(sender) + 1;
                    refusal = next > RidingRanks.Count
                        ? "Você já tem o nível máximo da habilidade."
                        : RiderServer.GrantRank(sender, next, "admin");
                    done = $"Você aprendeu {RidingRanks.NameOf(next)}.";
                    break;
                case ActionReset:
                    refusal = RiderServer.Reset(sender, "admin");
                    done = "Habilidade e montarias zeradas.";
                    break;
                default:
                    Plugin.Log.LogWarning($"ValheimMontarias: unknown rider action '{action}' from {who}");
                    return;
            }
            SendMessage(sender, refusal ?? done);
        }

        /// <summary>Eight requests per two seconds per peer: the menu asks once when it opens,
        /// so anything above that is a client asking on purpose.</summary>
        private static bool AllowRate(long sender)
        {
            float now = Time.realtimeSinceStartup;
            if (!Rate.TryGetValue(sender, out var window) || now - window.start > 2f)
                window = (now, 0);
            window.count++;
            Rate[sender] = window;
            return window.count <= 8;
        }

        internal static void SendState(long peer, RiderServer.Record record, string message)
        {
            if (record == null) return;
            Push(peer, record.Rank.ToString(CultureInfo.InvariantCulture) + "\n" + record.MountList, message);
        }

        private static void SendMessage(long peer, string message) => Push(peer, "", message);

        private static void Push(long peer, string state, string message) =>
            ZRoutedRpc.instance?.InvokeRoutedRPC(peer, RpcState, new object[] { state ?? "", message ?? "" });

        // ---- client ----

        private static void RPC_State(long sender, string state, string message)
        {
            if (!FromServer(sender)) return;

            if (!string.IsNullOrEmpty(state))
            {
                var parts = state.Split('\n');
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int rank))
                    RiderClient.Apply(rank, parts[1]);
                else
                    Plugin.Log.LogWarning($"ValheimMontarias: malformed rider state \"{state}\"");
            }

            if (!string.IsNullOrEmpty(message))
                RiderClient.Tell(message);
        }
    }

    /// <summary>
    /// The local player's level and mounts, as the server last said. Unknown until the first
    /// answer arrives: the menu says so instead of showing an empty journal, and summoning
    /// asks again.
    /// </summary>
    internal static class RiderClient
    {
        private static readonly HashSet<string> OwnedMounts = new HashSet<string>(StringComparer.Ordinal);
        private static float _lastRequest = -10f;

        public static bool Known { get; private set; }
        public static int Rank { get; private set; }

        /// <summary>Goes up with every change, so the menu can tell when to redraw.</summary>
        public static int Revision { get; private set; }

        public static string LastMessage { get; private set; }
        public static int MessageRevision { get; private set; }

        public static bool Owns(string mountId) => OwnedMounts.Contains(mountId ?? "");

        public static void Reset()
        {
            Known = false;
            Rank = 0;
            OwnedMounts.Clear();
            LastMessage = null;
            _lastRequest = -10f;
            Revision++;
        }

        /// <summary>Asks the server for this player's state. Throttled unless forced, because
        /// several things want it at once when the player spawns.</summary>
        public static void Request(bool force = false)
        {
            if (!force && Time.realtimeSinceStartup - _lastRequest < 3f) return;
            _lastRequest = Time.realtimeSinceStartup;
            RiderNet.Send(RiderNet.ActionState, "");
        }

        internal static void Apply(int rank, string mounts)
        {
            Known = true;
            Rank = Mathf.Max(0, rank);
            OwnedMounts.Clear();
            foreach (var id in (mounts ?? "").Split(','))
                if (id.Trim().Length > 0) OwnedMounts.Add(id.Trim());
            Revision++;
            Prefabs.MountHub.ApplySpeedAll();
        }

        internal static void Tell(string message)
        {
            LastMessage = message;
            MessageRevision++;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Start))]
    internal static class ZNet_Start_RiderNet_Patch
    {
        [HarmonyPostfix]
        private static void Postfix() => RiderNet.TryRegister();
    }
}
