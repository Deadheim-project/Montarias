using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// The boss pass of NpcValheim (NpcValheim.BossPassApi), bound by reflection like the
    /// other way round: a mount can ask for a boss's pass (the Moder asks for the Dragon's),
    /// won by helping kill the boss or bought at the Mestre dos Passes, and kept by the server.
    ///
    /// Without NpcValheim there are no passes, so the boss kill in the player's own profile
    /// counts instead -- the record Deadheim's PvP tier reads.
    /// </summary>
    internal static class BossPassBridge
    {
        private const string TypeName = "NpcValheim.BossPassApi";
        private const int KnownVersion = 1;
        private const float RequestEvery = 5f;

        private static bool _resolved;
        private static MethodInfo _known;
        private static MethodInfo _has;
        private static MethodInfo _request;
        private static MethodInfo _name;
        private static float _nextRequest;

        public static bool Available
        {
            get
            {
                Resolve();
                return _has != null;
            }
        }

        /// <summary>Whether there is anything to go by yet: with NpcValheim, once the server
        /// has said which passes this character holds (asked for on the way).</summary>
        public static bool Known
        {
            get
            {
                if (!Available) return true;
                if (Call(_known) is bool known && known) return true;
                Request();
                return false;
            }
        }

        public static bool Has(string boss)
        {
            if (string.IsNullOrEmpty(boss)) return true;
            if (!Available) return KilledLocally(boss);
            return Call(_has, boss) is bool has && has;
        }

        public static string NameOf(string boss)
        {
            if (string.IsNullOrEmpty(boss)) return "";
            if (Available && Call(_name, boss) is string name && name.Length > 0) return name;
            string token = EnemyToken(boss);
            if (token != null && Localization.instance != null) return Localization.instance.Localize(token);
            return boss;
        }

        /// <summary>Asks the server again, at most every few seconds: a pass won or bought
        /// meanwhile shows up the next time the player tries.</summary>
        public static void Request()
        {
            if (!Available || Time.unscaledTime < _nextRequest) return;
            _nextRequest = Time.unscaledTime + RequestEvery;
            Call(_request);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(TypeName, false))
                    .FirstOrDefault(t => t != null);
                if (type == null) return;
                var version = type.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                if (!(version?.GetRawConstantValue() is int v) || v != KnownVersion)
                {
                    Plugin.Log.LogWarning("ValheimMontarias: NpcValheim BossPassApi version not supported; boss passes come from the profile kills");
                    return;
                }
                _known = type.GetMethod("LocalKnown", BindingFlags.Public | BindingFlags.Static);
                _request = type.GetMethod("RequestLocalStatus", BindingFlags.Public | BindingFlags.Static);
                _name = type.GetMethod("BossName", BindingFlags.Public | BindingFlags.Static);
                var has = type.GetMethod("LocalHasPass", BindingFlags.Public | BindingFlags.Static);
                if (_known != null && _request != null && has != null)
                    _has = has;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: could not bind NpcValheim BossPassApi: {e.Message}");
            }
        }

        private static object Call(MethodInfo method, params object[] args)
        {
            if (method == null) return null;
            try
            {
                return method.Invoke(null, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: BossPassApi.{method.Name} failed: {e.InnerException?.Message ?? e.Message}");
                return null;
            }
        }

        /// <summary>The creature's internal name ($enemy_dragon) from its prefab (Dragon).</summary>
        private static string EnemyToken(string boss)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(boss) : null;
            var character = prefab != null ? prefab.GetComponent<Character>() : null;
            return character != null ? character.m_name : null;
        }

        private static bool KilledLocally(string boss)
        {
            string token = EnemyToken(boss);
            if (token == null) return false;
            try
            {
                var stats = Game.instance != null ? Game.instance.GetPlayerProfile()?.m_playerStats : null;
                var byModifier = stats != null && stats.Length > 0 ? stats[0]?.m_enemyStats : null;
                var kills = byModifier != null && byModifier.Length > 0 ? byModifier[0] : null;
                return kills != null && kills.TryGetValue(token, out float count) && count > 0f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: could not read the boss kills: {e.Message}");
                return false;
            }
        }
    }
}
