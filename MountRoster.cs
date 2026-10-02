using System.Collections.Generic;
using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// Which mounts a player owns. Purchases from the in-menu shop write a comma-separated
    /// id list on the player's ZDO. LiberarTodas still grants everything for testing.
    /// </summary>
    internal static class MountRoster
    {
        private const string ZdoKey = "vm_owned";

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

        public static bool TryBuy(Player player, MountProfile profile, out string message)
        {
            message = "";
            if (player == null || profile == null)
            {
                message = "Não foi possível comprar.";
                return false;
            }

            if (Owns(player, profile))
            {
                message = "Você já possui esta montaria.";
                return false;
            }

            int price = profile.ShopPrice != null ? Mathf.Max(0, profile.ShopPrice.Value) : 50;
            if (price > 0 && !MountWallet.TryPay(player, price))
            {
                message = $"Moedas insuficientes. Custa {price} coins.";
                return false;
            }

            Grant(player, profile);
            message = price > 0
                ? $"{profile.Name} adquirida por {price} coins."
                : $"{profile.Name} adquirida.";
            return true;
        }

        private static HashSet<string> OwnedIds(Player player)
        {
            var ids = new HashSet<string>();
            var zdo = ZdoOf(player);
            if (zdo == null) return ids;
            string raw = zdo.GetString(ZdoKey, "");
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
            var zdo = ZdoOf(player);
            if (zdo == null) return;
            zdo.Set(ZdoKey, string.Join(",", ids));
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
