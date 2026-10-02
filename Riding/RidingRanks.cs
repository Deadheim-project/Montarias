using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimMontarias
{
    internal sealed class RidingRank
    {
        public int Level;
        public string Name;
        public float Speed;
    }

    /// <summary>
    /// The levels of the riding skill, WoW style: the first one lets you summon at all, the
    /// next ones make every mount you ride faster. Read from [Habilidade] Niveis, synchronized
    /// from the server, so the shop (NpcValheim's Mestre das Montarias) and the game agree on
    /// what each level is called and does.
    /// </summary>
    internal static class RidingRanks
    {
        public const string Default = "Montaria Aprendiz:1|Montaria Experiente:1.25|Montaria Mestre:1.5";

        private static string _parsedFrom;
        private static List<RidingRank> _ranks = new List<RidingRank>();

        public static IReadOnlyList<RidingRank> All
        {
            get
            {
                string raw = MountSettings.RidingRankList != null ? MountSettings.RidingRankList.Value : Default;
                if (!string.Equals(raw, _parsedFrom, StringComparison.Ordinal))
                {
                    _parsedFrom = raw;
                    var problems = new List<string>();
                    _ranks = Parse(raw, problems);
                    foreach (var problem in problems)
                        Plugin.Log?.LogWarning("ValheimMontarias: [Habilidade] Niveis -- " + problem);
                }
                return _ranks;
            }
        }

        public static int Count => All.Count;

        public static RidingRank Get(int level)
        {
            var all = All;
            return level >= 1 && level <= all.Count ? all[level - 1] : null;
        }

        public static string NameOf(int level)
        {
            if (level <= 0) return "nenhuma";
            var rank = Get(level);
            return rank != null ? rank.Name : $"Nível {level}";
        }

        /// <summary>Speed multiplier of a level; levels past the list keep the last one's.</summary>
        public static float SpeedOf(int level)
        {
            if (level <= 0 || Count == 0) return 1f;
            return Get(Mathf.Min(level, Count)).Speed;
        }

        /// <summary>"nome:velocidade" entries separated by |. A level with no name or a speed
        /// that is not a number is skipped and named in the log; the speed is clamped to
        /// 0.5..3 so a typo cannot launch a mount across the map.</summary>
        internal static List<RidingRank> Parse(string raw, List<string> problems)
        {
            var ranks = new List<RidingRank>();
            if (string.IsNullOrWhiteSpace(raw)) return ranks;

            foreach (var entry in raw.Split('|'))
            {
                var text = entry.Trim();
                if (text.Length == 0) continue;

                string name = text;
                float speed = 1f;
                int colon = text.LastIndexOf(':');
                if (colon >= 0)
                {
                    name = text.Substring(0, colon).Trim();
                    var number = text.Substring(colon + 1).Trim().Replace(',', '.');
                    if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out speed))
                    {
                        problems?.Add($"\"{text}\": velocidade não é um número");
                        continue;
                    }
                }
                if (name.Length == 0)
                {
                    problems?.Add($"\"{text}\": nível sem nome");
                    continue;
                }

                ranks.Add(new RidingRank
                {
                    Level = ranks.Count + 1,
                    Name = name,
                    Speed = Mathf.Clamp(speed, 0.5f, 3f)
                });
            }
            return ranks;
        }
    }
}
