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
        /// <summary>Multiplies every mount's cast time (1 = no change, 0.5 = half).</summary>
        public float Cast = 1f;
    }

    /// <summary>
    /// The levels of the riding skill, WoW style: the first one lets you summon at all, the
    /// next ones make every mount you ride faster. Read from [Habilidade] Niveis, synchronized
    /// from the server, so the shop (NpcValheim's Mestre das Montarias) and the game agree on
    /// what each level is called and does.
    /// </summary>
    internal static class RidingRanks
    {
        public const string Default = "Montaria Aprendiz:1:1|Montaria Experiente:1.25:0.75|Montaria Mestre:1.5:0.5";

        /// <summary>The default before levels had a cast time; a config still on it is moved to
        /// <see cref="Default"/>, one an admin edited is left alone.</summary>
        public const string OldDefault = "Montaria Aprendiz:1|Montaria Experiente:1.25|Montaria Mestre:1.5";

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

        /// <summary>Cast time multiplier of a level; levels past the list keep the last one's.</summary>
        public static float CastOf(int level)
        {
            if (level <= 0 || Count == 0) return 1f;
            return Get(Mathf.Min(level, Count)).Cast;
        }

        /// <summary>"nome:velocidade:cast" entries separated by |, the cast being optional (1).
        /// A level with no name or a value that is not a number is skipped and named in the
        /// log; the speed is clamped to 0.5..3 so a typo cannot launch a mount across the map,
        /// and the cast to 0.1..1 so a level never makes summoning slower.</summary>
        internal static List<RidingRank> Parse(string raw, List<string> problems)
        {
            var ranks = new List<RidingRank>();
            if (string.IsNullOrWhiteSpace(raw)) return ranks;

            foreach (var entry in raw.Split('|'))
            {
                var text = entry.Trim();
                if (text.Length == 0) continue;

                var parts = text.Split(':');
                string name = text;
                float speed = 1f;
                float cast = 1f;
                if (parts.Length >= 3 && TryNumber(parts[parts.Length - 2], out _))
                {
                    name = string.Join(":", parts, 0, parts.Length - 2).Trim();
                    TryNumber(parts[parts.Length - 2], out speed);
                    if (!TryNumber(parts[parts.Length - 1], out cast))
                    {
                        problems?.Add($"\"{text}\": cast não é um número");
                        continue;
                    }
                }
                else if (parts.Length >= 2)
                {
                    name = string.Join(":", parts, 0, parts.Length - 1).Trim();
                    if (!TryNumber(parts[parts.Length - 1], out speed))
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
                    Speed = Mathf.Clamp(speed, 0.5f, 3f),
                    Cast = Mathf.Clamp(cast, 0.1f, 1f)
                });
            }
            return ranks;
        }

        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
