using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    internal static class WhistleItem
    {
        public const string PrefabName = "ApitoJavali";
        public const string DisplayName = "Apito da Capivara";
        public const string DisplayDesc =
            "U abre o menu, H invoca ou recolhe. Admin: aba Admin ou E na montaria.";

        public const string CentauroItemName = "ApitoCentauro";
        public const string CentauroDisplayName = "Apito do Centauro";
        public const string CentauroDisplayDesc =
            "U abre o menu, H invoca ou recolhe. Admin: aba Admin ou E na montaria.";

        internal static GameObject ItemPrefab { get; private set; }
        internal static GameObject CentauroItem { get; private set; }

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

        public static bool IsCentauro(ItemDrop.ItemData item)
        {
            if (!CanBeWhistle(item)) return false;
            if (item.m_dropPrefab != null)
                return item.m_dropPrefab.name == CentauroItemName;
            var name = item.m_shared.m_name;
            return name == CentauroDisplayName || name == "$item_apitocentauro";
        }

        public static bool IsWhistle(ItemDrop.ItemData item)
        {
            return IsCapybara(item) || IsCentauro(item);
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
            CentauroItem = RegisterOne(db, CentauroItem, CentauroItemName, CentauroDisplayName, CentauroDisplayDesc,
                "TrophyLox", "LoxPelt", "TrophyBoar", "Club");
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
            Access.AddScenePrefab(scene, CentauroItem);
        }

        public static void FinishScene(ZNetScene scene)
        {
            Access.EnsureNamedPrefab(scene, ItemPrefab);
            Access.EnsureNamedPrefab(scene, CentauroItem);
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
