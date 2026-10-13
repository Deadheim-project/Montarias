using System.Collections.Generic;
using System;

namespace ValheimMontarias
{
    /// <summary>
    /// What the local player may summon, WoW style: the riding skill at the level the mount
    /// asks for (<see cref="MountProfile.MinRank"/>) and the mount itself. Both are the
    /// server's record (<see cref="RiderServer"/>), as the client last heard it
    /// (<see cref="RiderClient"/>); they are sold by NpcValheim's Mestre das Montarias.
    /// LiberarTodasGratis grants every level and every mount.
    /// </summary>
    internal static class MountRoster
    {
        public static bool UnlockAll => MountSettings.UnlockAll != null && MountSettings.UnlockAll.Value;

        /// <summary>Whether there is anything to go by yet.</summary>
        public static bool Known => UnlockAll || RiderClient.Known;

        public static int Rank => UnlockAll ? RidingRanks.Count : RiderClient.Rank;

        public static float SpeedBonus => RidingRanks.SpeedOf(Rank);

        /// <summary>What the local player's level does to every cast time (1 = nothing).</summary>
        public static float CastMultiplier => RidingRanks.CastOf(Rank);

        public static bool Owns(MountProfile profile) =>
            profile != null && AllowedForLocalPlayer(profile) && (UnlockAll || RiderClient.Owns(profile.Id));

        public static bool CanRide(MountProfile profile) => profile != null && Rank >= profile.MinRank;

        public static bool Usable(MountProfile profile) => Owns(profile) && CanRide(profile) && BossDone(profile);

        /// <summary>Whether the local player holds the boss pass the mount asks for
        /// (<see cref="BossPassBridge"/>).</summary>
        public static bool BossDone(MountProfile profile) => BossPassBridge.Has(profile?.Boss);

        public static string BossName(MountProfile profile) => BossPassBridge.NameOf(profile?.Boss);

        public static bool AllowedForLocalPlayer(MountProfile profile)
        {
            if (profile == null)
                return false;

            if (profile.AllowedSteamIds == null || string.IsNullOrWhiteSpace(profile.AllowedSteamIds.Value))
                return true;

            var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in (profile.AllowedSteamIds.Value ?? string.Empty).Split(','))
            {
                var item = NormalizeSteamId(value);
                if (!string.IsNullOrWhiteSpace(item))
                    allow.Add(item);
            }

            if (allow.Count == 0)
                return true;

            foreach (var item in LocalSteamIds())
                if (allow.Contains(item))
                    return true;

            return false;
        }

        public static List<MountProfile> UsableMounts()
        {
            var list = new List<MountProfile>();
            var all = MountSettings.All;
            if (all == null) return list;
            for (int i = 0; i < all.Length; i++)
            {
                if (Usable(all[i]))
                    list.Add(all[i]);
            }
            return list;
        }

        /// <summary>Why a mount cannot be summoned right now, or null when it can.</summary>
        public static string Blocker(MountProfile profile)
        {
            if (profile == null) return "Você ainda não possui nenhuma montaria. Procure o Mestre das Montarias.";
            if (!AllowedForLocalPlayer(profile))
                return $"{profile.Name} está disponível apenas para SteamIDs autorizados.";
            if (!Known) return "Consultando o servidor, tente de novo em instantes.";
            if (!Owns(profile)) return $"Você não possui {profile.Name}. Procure o Mestre das Montarias.";
            if (!CanRide(profile))
                return $"{profile.Name} exige a habilidade {RidingRanks.NameOf(profile.MinRank)}. Aprenda com o Mestre das Montarias.";
            if (!string.IsNullOrEmpty(profile.Boss) && !BossPassBridge.Known)
                return "Consultando os passes no servidor, tente de novo em instantes.";
            if (!BossDone(profile))
            {
                BossPassBridge.Request();
                return $"{profile.Name} exige o passe de {BossName(profile)}: derrote o chefe ou compre o passe no Mestre dos Passes.";
            }
            return null;
        }

        private static HashSet<string> LocalSteamIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var player = Player.m_localPlayer;
            if (player == null)
                return ids;

            var raw = player.GetPlayerID().ToString();
            ids.Add(raw);
            ids.Add("Steam_" + raw);
            ids.Add("steam_" + raw);
            return ids;
        }

        private static string NormalizeSteamId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            if (trimmed.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            if (trimmed.StartsWith("steam_", StringComparison.OrdinalIgnoreCase))
                return "Steam_" + trimmed.Substring(6);

            return trimmed;
        }
    }
}
