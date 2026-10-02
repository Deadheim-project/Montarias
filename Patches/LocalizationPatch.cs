using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
    internal static class Localization_SetupLanguage_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Localization __instance)
        {
            Apply(__instance);
        }

        internal static void Apply(Localization loc)
        {
            if (loc == null) return;
            Add(loc, "item_apitojavali", "Apito da Capivara");
            Add(loc, "item_apitojavali_desc", WhistleItem.DisplayDesc);
        }

        private static void Add(Localization loc, string key, string value)
        {
            var addWord = typeof(Localization).GetMethod("AddWord", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (addWord != null)
            {
                addWord.Invoke(loc, new object[] { key, value });
                return;
            }

            foreach (var fieldName in new[] { "m_translations", "m_localizedStrings" })
            {
                var field = typeof(Localization).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(loc) is Dictionary<string, string> dict)
                {
                    dict[key] = value;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class Player_OnSpawned_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (Localization.instance != null)
                Localization_SetupLanguage_Patch.Apply(Localization.instance);
            RiderClient.Request(true);
        }
    }
}
