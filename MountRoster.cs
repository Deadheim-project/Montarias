using System.Collections.Generic;

namespace ValheimMontarias
{
    /// <summary>
    /// Which mounts a player owns: a comma-separated id list in Player.m_customData, which
    /// the game saves in the character file. The player's ZDO is recreated on every login,
    /// so a list kept only there was lost on relog; it is still read once and migrated.
    /// LiberarTodasGratis grants everything.
    /// </summary>
    internal static class MountRoster
    {
        private const string Key = "vm_owned";

        public static bool Owns(Player player, MountProfile profile)
        {
            if (player == null || profile == null) return false;
            if (MountSettings.UnlockAll != null && MountSettings.UnlockAll.Value)
                return true;
            return OwnedIds(player).Contains(profile.Id);
        }

        public static List<MountProfile> Owned(Player player)
        {
            var list = new List<MountProfile>();
            var all = MountSettings.All;
            if (all == null) return list;
            for (int i = 0; i < all.Length; i++)
            {
                if (Owns(player, all[i]))
                    list.Add(all[i]);
            }
            return list;
        }

        public static void Grant(Player player, MountProfile profile)
        {
            if (player == null || profile == null) return;
            var ids = OwnedIds(player);
            if (!ids.Add(profile.Id)) return;
            Save(player, ids);
        }

        private static HashSet<string> OwnedIds(Player player)
        {
            var ids = new HashSet<string>();
            string raw = null;
            var data = CustomData(player);
            if (data != null)
                data.TryGetValue(Key, out raw);
            if (string.IsNullOrEmpty(raw))
            {
                var zdo = ZdoOf(player);
                raw = zdo != null ? zdo.GetString(Key, "") : "";
                if (!string.IsNullOrEmpty(raw) && data != null)
                    data[Key] = raw;
            }
            if (string.IsNullOrEmpty(raw)) return ids;
            var parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim();
                if (id.Length > 0) ids.Add(id);
            }
            return ids;
        }

        private static void Save(Player player, HashSet<string> ids)
        {
            string raw = string.Join(",", ids);
            var data = CustomData(player);
            if (data != null)
                data[Key] = raw;
            var zdo = ZdoOf(player);
            if (zdo != null)
                zdo.Set(Key, raw);
        }

        private static IDictionary<string, string> CustomData(Player player)
        {
            return Access.Get(player, "m_customData") as IDictionary<string, string>;
        }

        private static ZDO ZdoOf(Player player)
        {
            if (player == null) return null;
            var nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return null;
            return nview.GetZDO();
        }
    }
}
