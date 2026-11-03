using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValheimMontarias.Pets
{
    /// <summary>
    /// Fala com Deadheim.Pets (mod Deadheim) por reflexão, só com tipos primitivos, para que
    /// nenhum dos dois precise do outro para compilar ou carregar.
    /// </summary>
    internal static class PetBridge
    {
        private static Type _type;
        private static float _nextLookup;
        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();

        private static Type PetsType
        {
            get
            {
                if (_type != null) return _type;
                if (Time.realtimeSinceStartup < _nextLookup) return null;
                _nextLookup = Time.realtimeSinceStartup + 5f;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = assembly.GetType("Deadheim.Pets", false); } catch { }
                    if (t != null && t.GetMethod("ToggleQuick", BindingFlags.Public | BindingFlags.Static) != null)
                    {
                        _type = t;
                        break;
                    }
                }
                return _type;
            }
        }

        public static bool Available => PetsType != null;

        private static object Call(string name, params object[] args)
        {
            var type = PetsType;
            if (type == null) return null;
            try
            {
                string key = name + "/" + args.Length;
                if (!Methods.TryGetValue(key, out var method))
                {
                    var types = new Type[args.Length];
                    for (int i = 0; i < types.Length; i++) types[i] = typeof(string);
                    method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
                    Methods[key] = method;
                }
                return method?.Invoke(null, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: Deadheim.Pets.{name} falhou: {e.GetBaseException().Message}");
                return null;
            }
        }

        public static string[] CatalogIds() => Call("CatalogIds") as string[] ?? new string[0];
        public static bool IsUnlocked(string id) => Call("IsUnlocked", id) is bool b && b;
        public static bool HasItem(string id) => Call("HasItem", id) is bool b && b;
        public static string ActiveId() => Call("ActiveId") as string ?? "";

        /// <summary>Pet cujos bônus valem agora (ativo e fora de arena/raid), ou "".</summary>
        public static string ActiveIdForBuffs() => Call("ActiveIdForBuffs") as string ?? "";

        public static bool Activate(string id) => Call("Activate", id) is bool b && b;
        public static void Dismiss() => Call("Dismiss");
        public static void ToggleQuick() => Call("ToggleQuick");

        /// <summary>Nome do pet a partir do item Pet_&lt;id&gt; ("Pet: Galinha" vira "Galinha").</summary>
        public static string NameOf(string id)
        {
            var shared = SharedOf(id);
            if (shared == null) return id;
            string name = Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name;
            const string prefix = "Pet: ";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static ItemDrop.ItemData.SharedData SharedOf(string id)
        {
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Pet_" + id) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared : null;
        }
    }
}
