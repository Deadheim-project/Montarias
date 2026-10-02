using System.Collections.Generic;
using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// Temporary wallet: vanilla Coins. A dedicated currency bought with real money comes later.
    /// </summary>
    internal static class MountWallet
    {
        public const string CoinPrefab = "Coins";
        public const string CoinShared = "$item_coins";

        public static int Count(Player player)
        {
            var inv = player != null ? player.GetInventory() : null;
            if (inv == null) return 0;
            int n = 0;
            var items = inv.GetAllItems();
            if (items == null) return 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (IsCoin(item)) n += item.m_stack;
            }
            return n;
        }

        public static bool TryPay(Player player, int amount)
        {
            if (player == null || amount < 0) return false;
            if (amount == 0) return true;
            if (Count(player) < amount) return false;

            var inv = player.GetInventory();
            if (inv == null) return false;

            int before = Count(player);
            inv.RemoveItem(CoinShared, amount);
            if (Count(player) <= before - amount)
                return true;

            inv.RemoveItem(CoinPrefab, amount);
            if (Count(player) <= before - amount)
                return true;

            int left = amount;
            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            for (int i = 0; i < items.Count && left > 0; i++)
            {
                var item = items[i];
                if (!IsCoin(item)) continue;
                int take = Mathf.Min(left, item.m_stack);
                item.m_stack -= take;
                left -= take;
                if (item.m_stack <= 0)
                    inv.RemoveItem(item);
            }

            return left <= 0;
        }

        private static bool IsCoin(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            if (item.m_shared.m_name == CoinShared) return true;
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            return prefab == CoinPrefab;
        }
    }
}
