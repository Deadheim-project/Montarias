using System.Collections.Generic;

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

        public static bool Owns(MountProfile profile) =>
            profile != null && (UnlockAll || RiderClient.Owns(profile.Id));

        public static bool CanRide(MountProfile profile) => profile != null && Rank >= profile.MinRank;

        public static bool Usable(MountProfile profile) => Owns(profile) && CanRide(profile);

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
            if (!Known) return "Consultando o servidor, tente de novo em instantes.";
            if (!Owns(profile)) return $"Você não possui {profile.Name}. Procure o Mestre das Montarias.";
            if (!CanRide(profile))
                return $"{profile.Name} exige a habilidade {RidingRanks.NameOf(profile.MinRank)}. Aprenda com o Mestre das Montarias.";
            return null;
        }
    }
}
