using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimMontarias.Pets
{
    /// <summary>
    /// Bônus simples do pet ativo. Cada pet tem os seus na config (PetSettings.BuffTable);
    /// valem só com o pet invocado e fora das zonas em que ele fica oculto (arena, raid), então
    /// não mexem no PvP. Tudo é cliente: peso, stamina e descanso do próprio jogador.
    /// </summary>
    internal static class PetBuffs
    {
        public const string Weight = "peso";
        public const string Rested = "descanso";
        public const string Wood = "madeira";
        public const string Metal = "metal";
        public const string Pickup = "coleta";
        public const string Craft = "oficio";
        // PvE: só valem contra criaturas (jogador e criatura domada ficam de fora, então não mexem no PvP).
        public const string DamagePve = "danopve";
        public const string DefensePve = "defesapve";
        public const string LifeSteal = "roubovida";
        public const string Vigor = "vigorpve";

        // Um bônus diferente para cada pet, sem repetir.
        public const string DefaultTable =
            "galinha=descanso:20," +
            "pintinho=coleta:30," +
            "leitao=madeira:25," +
            "wolf_cub=metal:15," +
            "lox_cub=peso:5," +
            "corvo=oficio:15," +
            "foca=defesapve:8," +
            "ursinho=danopve:8," +
            "gaivota=roubovida:3," +
            "veado=vigorpve:3";

        public const string KeysHelp =
            "peso (capacidade de carga +%), descanso (tempo de Descansado +%), madeira (peso da madeira -%), " +
            "metal (peso de minério/metal/pedra -%), coleta (alcance de coleta automática +%), " +
            "oficio (ganho de habilidade de ofício +%), danopve (dano contra criaturas +%), " +
            "defesapve (dano recebido de criaturas -%), roubovida (% do dano causado em criaturas vira vida), " +
            "vigorpve (cada golpe em criatura recupera esta stamina)";

        private static readonly string[] Order = { Weight, Rested, Wood, Metal, Pickup, Craft, DamagePve, DefensePve, LifeSteal, Vigor };

        // petId -> (bônus -> %)
        private static Dictionary<string, Dictionary<string, float>> _table;
        private static string _parsedFrom;

        private static Dictionary<string, Dictionary<string, float>> Table
        {
            get
            {
                string raw = PetSettings.BuffTable != null ? PetSettings.BuffTable.Value : DefaultTable;
                if (_table != null && ReferenceEquals(raw, _parsedFrom)) return _table;
                _parsedFrom = raw;
                _table = Parse(raw);
                return _table;
            }
        }

        private static Dictionary<string, Dictionary<string, float>> Parse(string raw)
        {
            var result = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return result;
            foreach (var entry in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = entry.Split(new[] { '=' }, 2);
                if (kv.Length != 2 || kv[0].Trim().Length == 0) continue;
                var buffs = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in kv[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var bv = item.Split(new[] { ':' }, 2);
                    if (bv.Length != 2) continue;
                    if (!float.TryParse(bv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value <= 0f)
                        continue;
                    if (Array.IndexOf(Order, bv[0].Trim().ToLowerInvariant()) < 0) continue;
                    buffs[bv[0].Trim().ToLowerInvariant()] = Math.Min(value, 90f);
                }
                if (buffs.Count > 0) result[kv[0].Trim()] = buffs;
            }
            return result;
        }

        /// <summary>Percentual do bônus do pet ativo agora (0 se não há pet ativo ou ele não tem esse bônus).</summary>
        public static float Percent(string buff)
        {
            if (PetSettings.BuffsEnabled == null || !PetSettings.BuffsEnabled.Value || Player.m_localPlayer == null)
                return 0f;
            string pet = PetBridge.ActiveIdForBuffs();
            if (pet.Length == 0) return 0f;
            return Table.TryGetValue(pet, out var buffs) && buffs.TryGetValue(buff, out float value) ? value : 0f;
        }

        /// <summary>Linhas legíveis dos bônus de um pet, para o menu.</summary>
        public static List<string> Describe(string petId)
        {
            var lines = new List<string>();
            if (!Table.TryGetValue(petId ?? "", out var buffs)) return lines;
            foreach (var key in Order)
            {
                if (!buffs.TryGetValue(key, out float v)) continue;
                lines.Add(Label(key, v));
            }
            return lines;
        }

        private static string Label(string key, float v)
        {
            string n = v.ToString("0.#", CultureInfo.InvariantCulture);
            switch (key)
            {
                case Weight: return L.T("+{0}% de capacidade de peso", "+{0}% carry weight", n);
                case Rested: return L.T("+{0}% de duração do Descansado", "+{0}% Rested duration", n);
                case Wood: return L.T("-{0}% de peso da madeira", "-{0}% wood weight", n);
                case Metal: return L.T("-{0}% de peso de minério, metal e pedra", "-{0}% ore, metal and stone weight", n);
                case Pickup: return L.T("+{0}% de alcance para recolher itens do chão", "+{0}% item pickup range", n);
                case Craft: return L.T("+{0}% de ganho de skill de ofício", "+{0}% crafting skill gain", n);
                case DamagePve: return L.T("+{0}% de dano contra criaturas (PvE)", "+{0}% damage against creatures (PvE)", n);
                case DefensePve: return L.T("-{0}% de dano recebido de criaturas (PvE)", "-{0}% damage taken from creatures (PvE)", n);
                case LifeSteal: return L.T("{0}% do dano causado em criaturas vira vida (PvE)", "{0}% of damage dealt to creatures heals you (PvE)", n);
                case Vigor: return L.T("Cada golpe em criatura recupera {0} de stamina (PvE)", "Each hit on a creature restores {0} stamina (PvE)", n);
                default: return key;
            }
        }

        private static bool IsLocal(Player p) => p != null && p == Player.m_localPlayer;

        // ---- peso máximo ----
        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        private static class MaxCarryPatch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (!IsLocal(__instance)) return;
                float p = Percent(Weight);
                if (p > 0f) __result *= 1f + p / 100f;
            }
        }

        // ---- madeira mais leve (o peso do item entra na soma do inventário) ----
        private static readonly HashSet<string> WoodItems = new HashSet<string>
        {
            "$item_wood", "$item_finewood", "$item_corewood", "$item_elderbark", "$item_yggdrasilwood", "$item_blackwood"
        };

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
        private static class WoodWeightPatch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                if (__instance?.m_shared == null || !WoodItems.Contains(__instance.m_shared.m_name)) return;
                float p = Percent(Wood);
                if (p > 0f) __result *= 1f - p / 100f;
            }
        }

        // ---- minério, metal e pedra mais leves ----
        private static readonly HashSet<string> MetalItems = new HashSet<string>
        {
            "$item_stone", "$item_copperore", "$item_tinore", "$item_ironscrap", "$item_silverore",
            "$item_blackmetalscrap", "$item_flametalore", "$item_copper", "$item_tin", "$item_bronze",
            "$item_iron", "$item_silver", "$item_blackmetal", "$item_flametal"
        };

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
        private static class MetalWeightPatch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                if (__instance?.m_shared == null || !MetalItems.Contains(__instance.m_shared.m_name)) return;
                float p = Percent(Metal);
                if (p > 0f) __result *= 1f - p / 100f;
            }
        }

        // ---- alcance de coleta automática dos itens no chão ----
        private static float _basePickupRange = -1f;

        [HarmonyPatch(typeof(Player), "AutoPickup")]
        private static class PickupRangePatch
        {
            private static void Prefix(Player __instance)
            {
                if (!IsLocal(__instance)) return;
                if (_basePickupRange < 0f) _basePickupRange = __instance.m_autoPickupRange;
                float p = Percent(Pickup);
                __instance.m_autoPickupRange = _basePickupRange * (1f + p / 100f);
            }
        }

        // ---- habilidades de ofício sobem mais rápido (nada de combate) ----
        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class CraftSkillPatch
        {
            private static void Prefix(Skills __instance, Skills.SkillType skillType, ref float factor)
            {
                if (Player.m_localPlayer == null || Player.m_localPlayer.GetSkills() != __instance) return;
                switch (skillType)
                {
                    case Skills.SkillType.WoodCutting:
                    case Skills.SkillType.Pickaxes:
                    case Skills.SkillType.Fishing:
                    case Skills.SkillType.Farming:
                    case Skills.SkillType.Cooking:
                    case Skills.SkillType.Crafting:
                        float p = Percent(Craft);
                        if (p > 0f) factor *= 1f + p / 100f;
                        break;
                }
            }
        }

        // ---- PvE: dano causado. Roda no cliente do atacante, antes do golpe virar RPC. ----
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class DamagePvePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit == null || __instance == null || __instance.IsPlayer() || __instance.IsTamed()) return;
                if (hit.GetAttacker() != Player.m_localPlayer) return;
                float p = Percent(DamagePve);
                if (p > 0f) hit.m_damage.Modify(1f + p / 100f);
                float steal = Percent(LifeSteal);
                if (steal > 0f && Player.m_localPlayer != null)
                    Player.m_localPlayer.Heal(hit.GetTotalDamage() * steal / 100f, true);
                float vigor = Percent(Vigor);
                if (vigor > 0f && Player.m_localPlayer != null) Player.m_localPlayer.AddStamina(vigor);
            }
        }

        // ---- PvE: dano recebido. RPC_Damage roda no dono da vítima, o cliente do próprio jogador. ----
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class DefensePvePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit == null || !IsLocal(__instance as Player)) return;
                Character attacker = hit.GetAttacker();
                if (attacker == null || attacker.IsPlayer() || attacker.IsTamed()) return;
                float p = Percent(DefensePve);
                if (p > 0f) hit.m_damage.Modify(Mathf.Max(0f, 1f - p / 100f));
            }
        }

        // ---- Descansado dura mais ----
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.UpdateTTL))]
        private static class RestedPatch
        {
            // UpdateTTL só regrava m_ttl quando o novo valor passa do que resta; multiplicar a cada
            // chamada acumularia. O bônus entra só quando m_ttl acabou de ser regravado.
            private static void Prefix(SE_Rested __instance, out float __state) => __state = __instance.m_ttl;

            private static void Postfix(SE_Rested __instance, float __state)
            {
                if (!IsLocal(__instance.m_character as Player)) return;
                if (Mathf.Approximately(__instance.m_ttl, __state)) return;
                float p = Percent(Rested);
                if (p > 0f) __instance.m_ttl *= 1f + p / 100f;
            }
        }
    }
}
