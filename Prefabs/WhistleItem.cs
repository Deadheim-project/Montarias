using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    internal static class WhistleItem
    {
        public const string PrefabName = "ApitoJavali";
        public const string DisplayName = "Apito da Capivara";
        public const string DisplayDesc =
            "Use para montar na Capivara; use de novo (ou E) para descer. Requer a Habilidade de Montaria.";

        internal static GameObject ItemPrefab { get; private set; }

        private static Transform _hidden;

        private static Transform Hidden
        {
            get
            {
                if (_hidden != null) return _hidden;
                var go = new GameObject("ValheimMontarias_HiddenItems");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _hidden = go.transform;
                return _hidden;
            }
        }

        public static bool IsCapybara(ItemDrop.ItemData item)
        {
            if (!CanBeWhistle(item)) return false;
            if (item.m_dropPrefab != null)
                return item.m_dropPrefab.name == PrefabName;
            var name = item.m_shared.m_name;
            return name == DisplayName || name == "Apito do Javali" || name == "$item_apitojavali";
        }

        public static bool IsWhistle(ItemDrop.ItemData item)
        {
            return ProfileOf(item) != null;
        }

        /// <summary>The mount an item stands for, or null when it is not a mount item.</summary>
        public static MountProfile ProfileOf(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return null;
            var byPrefab = item.m_dropPrefab != null ? MountSettings.ByItem(item.m_dropPrefab.name) : null;
            if (byPrefab != null) return byPrefab;
            return IsCapybara(item) ? MountSettings.Javali : null;
        }

        public static bool Has(Player player, MountProfile profile)
        {
            var items = player?.GetInventory()?.GetAllItems();
            if (items == null || profile == null) return false;
            foreach (var item in items)
                if (ProfileOf(item) == profile) return true;
            return false;
        }

        /// <summary>Puts the mount item in the player's bag, or at their feet when the bag is
        /// full. The item is only a way to use the mount: it does nothing for anyone who does
        /// not own that mount on the server, so a copy is worth nothing to somebody else.</summary>
        public static bool GiveTo(Player player, MountProfile profile)
        {
            if (player == null || profile == null) return false;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(profile.ItemPrefab) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop?.m_itemData == null)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: mount item {profile.ItemPrefab} not registered");
                return false;
            }

            var data = drop.m_itemData.Clone();
            data.m_stack = 1;
            data.m_dropPrefab = prefab;
            var inventory = player.GetInventory();
            if (inventory != null && inventory.AddItem(data))
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Recebido: {data.m_shared.m_name}", 1, null);
                return true;
            }

            ItemDrop.DropItem(data, 1, player.transform.position + Vector3.up, Quaternion.identity);
            player.Message(MessageHud.MessageType.Center, $"Inventário cheio: {data.m_shared.m_name} caiu no chão");
            return true;
        }

        private static bool CanBeWhistle(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            var type = item.m_shared.m_itemType;
            if (type == ItemDrop.ItemData.ItemType.Utility) return false;
            if (type == ItemDrop.ItemData.ItemType.OneHandedWeapon) return false;
            if (type == ItemDrop.ItemData.ItemType.TwoHandedWeapon) return false;
            if (type == ItemDrop.ItemData.ItemType.Bow) return false;
            if (type == ItemDrop.ItemData.ItemType.Attach_Atgeir) return false;
            return type == ItemDrop.ItemData.ItemType.Misc || type == ItemDrop.ItemData.ItemType.None;
        }

        public static void RegisterItem(ObjectDB db)
        {
            if (db == null) return;
            ItemPrefab = RegisterOne(db, ItemPrefab, PrefabName, DisplayName, DisplayDesc,
                "TrophyBoar", "BoarHide", "Club", "Wood");
        }

        private static GameObject RegisterOne(ObjectDB db, GameObject cached, string prefabName, string displayName, string desc, params string[] templates)
        {
            if (db.GetItemPrefab(prefabName) != null)
            {
                var existing = db.GetItemPrefab(prefabName);
                ApplyStats(existing, displayName, desc);
                return existing;
            }

            if (cached != null)
            {
                Access.AddItemPrefab(db, cached);
                ApplyStats(cached, displayName, desc);
                return cached;
            }

            GameObject source = null;
            for (int i = 0; i < templates.Length && source == null; i++)
                source = db.GetItemPrefab(templates[i]);
            if (source == null)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: no item template yet for {prefabName}");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(source, Hidden);
            clone.name = prefabName;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared == null)
            {
                Plugin.Log.LogError($"ValheimMontarias: {prefabName} clone has no ItemDrop");
                return null;
            }

            drop.m_itemData.m_dropPrefab = clone;
            drop.m_itemData.m_quality = 1;
            drop.m_itemData.m_stack = 1;
            ApplyStats(clone, displayName, desc);
            Access.AddItemPrefab(db, clone);
            Plugin.Log.LogInfo($"ValheimMontarias: whistle {prefabName} registered");
            return clone;
        }

        public static void RegisterScene(ZNetScene scene)
        {
            Access.AddScenePrefab(scene, ItemPrefab);
        }

        public static void FinishScene(ZNetScene scene)
        {
            Access.EnsureNamedPrefab(scene, ItemPrefab);
        }

        private static void ApplyStats(GameObject prefab, string displayName, string desc)
        {
            var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared == null) return;
            shared.m_name = displayName;
            shared.m_description = desc;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc;
            shared.m_maxStackSize = 1;
            shared.m_maxQuality = 1;
            shared.m_weight = 0.3f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            Access.Set(shared, "m_useItemStands", false);
            Access.Set(shared, "m_questItem", false);
        }
    }
}
