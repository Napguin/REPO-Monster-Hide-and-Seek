using System.Collections.Generic;

namespace HideAndSeekMonster
{
    internal enum AbilityType
    {
        None = 0,
        Laser = 1,     // Clown: the game's laser
        Shot = 2,      // long range single shot
        Sprint = 3,    // "Rapid Charge": double speed, damages everyone close
        Leap = 4,      // jump forward, kills on contact with a larger reach
        Reveal = 5,    // "Radar Ping": shows hiders in range, they get warned
        Throw = 6,     // throw a projectile that kills on hit
        Shockwave = 7, // stun / slow every hider nearby
        FlyingCharge = 8 // Apex Predator: fly + damage everyone close
    }

    internal static class Abilities
    {
        // Which special each monster of the game gets (by in-game name, lower case).
        private static readonly Dictionary<string, AbilityType> ByMonster = new Dictionary<string, AbilityType>
        {
            { "clown", AbilityType.Laser },
            { "huntsman", AbilityType.Shot },
            { "robe", AbilityType.Sprint },
            { "reaper", AbilityType.Sprint },
            { "animal", AbilityType.Sprint },
            { "headman", AbilityType.Leap },
            { "headgrab", AbilityType.Leap },
            { "upscream", AbilityType.Leap },
            { "snatcher", AbilityType.Leap },
            { "chef", AbilityType.Leap },
            { "tick", AbilityType.Leap },
            { "apex predator", AbilityType.FlyingCharge },
            { "peeper", AbilityType.Reveal },
            { "mentalist", AbilityType.Reveal },
            { "loom", AbilityType.Reveal },
            { "oogly", AbilityType.Reveal },
            { "elsa", AbilityType.Reveal },
            { "rugrat", AbilityType.Throw },
            { "cleanup crew", AbilityType.Throw },
            { "spewer", AbilityType.Throw },
            { "birthday boy", AbilityType.Throw },
            { "bowtie", AbilityType.Shockwave },
            { "trudge", AbilityType.Shockwave },
            { "heart hugger", AbilityType.Shockwave },
            { "gambit", AbilityType.Shockwave },
            { "bella", AbilityType.Shockwave },
        };

        public static AbilityType For(MonsterCatalog.Entry e)
        {
            if (e == null) return AbilityType.Sprint;
            if (e.HasLaser) return AbilityType.Laser;
            return ByMonster.TryGetValue(e.Name.ToLowerInvariant(), out var a) ? a : AbilityType.Sprint;
        }

        /// <summary>Cooldown in seconds after use (laser: see config).</summary>
        public static float Cooldown(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return Plugin.LaserCooldown.Value + Plugin.LaserDuration.Value;
                case AbilityType.Shot: return 15f;
                case AbilityType.Sprint: return 15f;
                case AbilityType.FlyingCharge: return 18f;
                case AbilityType.Leap: return 8f;
                case AbilityType.Reveal: return 20f;
                case AbilityType.Throw: return 5f;
                case AbilityType.Shockwave: return 15f;
                default: return 5f;
            }
        }

        public static float Duration(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return Plugin.LaserDuration.Value;
                case AbilityType.Sprint: return 5f;
                case AbilityType.FlyingCharge: return 6f;
                case AbilityType.Leap: return 1.2f;
                case AbilityType.Reveal: return 5f;
                case AbilityType.Shockwave: return 2.5f; // stun time
                default: return 0f;
            }
        }

        // ---- damage values (players have 100 HP) ----
        public const int ShotDamage = 60, ThrowDamage = 40, LeapDamage = 40;
        public const int LaserDamage = 25;          // per tick
        public const int RapidChargeDamage = 20;    // per tick
        public const int FlyingChargeDamage = 10;   // per tick
        public const float DamageTick = 0.5f;
        public const float ChargeRadius = 2.2f;
        public const float RadarRange = 30f;

        public static bool IsCharge(AbilityType a) => a == AbilityType.Sprint || a == AbilityType.FlyingCharge;
        public static int ChargeDamage(AbilityType a) => a == AbilityType.FlyingCharge ? FlyingChargeDamage : RapidChargeDamage;
        public static float ChargeSpeed(AbilityType a) => a == AbilityType.FlyingCharge ? 1.5f : 2f;

        public static string Name(AbilityType a) => L.AbilityName(a);
        public static string Description(AbilityType a) => L.AbilityDescription(a);

        /// <summary>Rich-text colour per ability (menus / HUD).</summary>
        public static string Color(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return "#ff5a3c";
                case AbilityType.Shot: return "#ffd34d";
                case AbilityType.Sprint: return "#5ad1ff";
                case AbilityType.Leap: return "#9dff5a";
                case AbilityType.Reveal: return "#d68cff";
                case AbilityType.Throw: return "#ffb347";
                case AbilityType.Shockwave: return "#6f8cff";
                case AbilityType.FlyingCharge: return "#ff6fd0";
                default: return "#a89a9c";
            }
        }

        public static UnityEngine.Color UnityColor(AbilityType a)
        {
            UnityEngine.ColorUtility.TryParseHtmlString(Color(a), out var c);
            return c;
        }
    }
}
