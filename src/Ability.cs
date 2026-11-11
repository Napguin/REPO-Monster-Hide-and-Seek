using System.Linq;
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
        FlyingCharge = 8, // Apex Predator: fly + damage everyone close (attack form)
        Bomb = 9,        // Cleanup Crew: throws its head – explosion + uranium cloud
        Puke = 10,       // Spewer: vomit stream in front of it
        Levitate = 11,   // Mentalist: lifts hiders up and slams them down
        Slam = 12,       // Trudge: ground slam with knock-back
        Pull = 13,       // Heart Hugger: gas that pulls hiders towards it
        Gamble = 14,     // Gambit: random special
        FlyingGrab = 15, // Oogly: invisible bubble that stuns hiders around it
        ZeroGGrab = 16,  // Mentalist: stun bubble + zero gravity inside
        NoxiousGas = 17, // Heart Hugger: 5 big lingering gas clouds that tranquilize hiders
        Swarm = 18,      // Gnome / Banger: summon 5 allies that hunt hiders on their own
        Charge = 19,     // Robe / Headgrab: freeze, then dash forward horizontally (60 damage on contact)
        Lunge = 20,      // Headman / Chef: like Charge, but in the camera direction (also up / in the air)
        ItemThrow = 21   // Rugrat: pick up a valuable, use again to throw it very fast
    }

    internal static class Abilities
    {
        // Which special each monster of the game gets (by in-game name, lower case).
        private static readonly Dictionary<string, AbilityType> ByMonster = new Dictionary<string, AbilityType>
        {
            { "clown", AbilityType.Laser },
            { "huntsman", AbilityType.Shot },
            { "robe", AbilityType.Charge },
            { "reaper", AbilityType.Sprint },
            { "animal", AbilityType.Sprint },
            { "headman", AbilityType.Lunge },
            { "headgrab", AbilityType.Charge },
            { "upscream", AbilityType.Leap },
            { "snatcher", AbilityType.Leap },
            { "chef", AbilityType.Lunge },
            { "tick", AbilityType.Leap },
            { "apex predator", AbilityType.FlyingCharge },
            { "peeper", AbilityType.Reveal },
            { "mentalist", AbilityType.ZeroGGrab },
            { "loom", AbilityType.Reveal },
            { "oogly", AbilityType.FlyingGrab },
            { "elsa", AbilityType.Reveal },
            { "rugrat", AbilityType.ItemThrow },
            { "cleanup crew", AbilityType.Bomb },
            { "spewer", AbilityType.Puke },
            { "birthday boy", AbilityType.Throw },
            { "bowtie", AbilityType.Shockwave },
            { "trudge", AbilityType.Slam },
            { "heart hugger", AbilityType.NoxiousGas },
            { "gnome", AbilityType.Swarm },
            { "banger", AbilityType.Swarm },
            { "gambit", AbilityType.Gamble },
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
                case AbilityType.Bomb: return 10f;
                case AbilityType.Puke: return 12f;
                case AbilityType.Levitate: return 20f;
                case AbilityType.Slam: return 10f;
                case AbilityType.Pull: return 16f;
                case AbilityType.Gamble: return 15f;
                case AbilityType.FlyingGrab: return 20f;
                case AbilityType.ZeroGGrab: return 18f;
                case AbilityType.NoxiousGas: return 25f;
                case AbilityType.Swarm: return 40f;
                case AbilityType.Charge: return 10f;
                case AbilityType.Lunge: return 10f;
                case AbilityType.ItemThrow: return 6f;
                case AbilityType.Leap: return 8f;
                case AbilityType.Reveal: return 120f;
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
                case AbilityType.Puke: return 2.5f;
                case AbilityType.Levitate: return 1.6f;  // time in the air before the slam
                case AbilityType.Slam: return 0.6f;      // wind-up
                case AbilityType.Pull: return 3f;
                case AbilityType.FlyingGrab: return 8f;
                case AbilityType.ZeroGGrab: return 4f;
                case AbilityType.Leap: return 1.2f;
                case AbilityType.Reveal: return 2f;
                case AbilityType.Shockwave: return 2.5f; // stun time
                default: return 0f;
            }
        }

        // ---- damage values (players have 100 HP) ----
        public const int ShotDamage = 60, ThrowDamage = 40, LeapDamage = 40;
        public const int LaserDamage = 50;          // per tick
        public const int RapidChargeDamage = 20;    // per tick
        public const int FlyingChargeDamage = 10;   // per tick
        public const float DamageTick = 0.5f;
        public const float ChargeRadius = 2.2f;
        public const float RadarRange = 30f;
        public const int BombDamage = 50, CloudDamage = 10, PukeDamage = 10, LevitateDamage = 30, SlamDamage = 50;
        public const float BombRadius = 4f, CloudRadius = 3f, CloudTime = 5f;
        public const float PukeRange = 7f, LevitateRange = 10f, SlamRadius = 5f, PullRange = 12f;

        /// <summary>Abilities Gambit can roll.</summary>
        /// <summary>Abilities Gambit can roll: every special in the mod (new ones are included automatically).</summary>
        public static readonly AbilityType[] GambleTable =
            ((AbilityType[])System.Enum.GetValues(typeof(AbilityType)))
            .Where(a => a != AbilityType.None && a != AbilityType.Gamble && a != AbilityType.ItemThrow).ToArray(); // item throw needs two presses

        public static bool IsCharge(AbilityType a) => a == AbilityType.Sprint || a == AbilityType.FlyingCharge;
        public const int DashDamage = 60;
        public const float DashWindup = 0.5f, DashSpeed = 24f, DashHitRadius = 1.8f;
        public static float DashTime(AbilityType a) => a == AbilityType.Charge ? 0.6f : 0.35f; // Charge ~14 m, Lunge ~8 m
        public static bool IsDash(AbilityType a) => a == AbilityType.Charge || a == AbilityType.Lunge;

        public const int GasClouds = 5;
        public const float GasTime = 10f, GasRadius = 1.8f, GasSpread = 4f;

        public static bool IsGrab(AbilityType a) => a == AbilityType.FlyingGrab || a == AbilityType.ZeroGGrab;
        public static float GrabRadius(AbilityType a) => a == AbilityType.ZeroGGrab ? 6f : 4f;

        /// <summary>Monsters that can always fly (jump = up, crouch = down).</summary>
        public static bool FreeFlight(string monster)
        {
            var n = (monster ?? "").ToLowerInvariant();
            return n == "oogly" || n == "spewer";
        }
        public static int ChargeDamage(AbilityType a) => a == AbilityType.FlyingCharge ? FlyingChargeDamage : RapidChargeDamage;
        public static float ChargeSpeed(AbilityType a) => 1.5f;

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
                case AbilityType.Bomb: return "#7dff3a";
                case AbilityType.Puke: return "#b5c93a";
                case AbilityType.Levitate: return "#c9a0ff";
                case AbilityType.Slam: return "#d08a4a";
                case AbilityType.Pull: return "#ff5f8f";
                case AbilityType.Gamble: return "#ffe14d";
                case AbilityType.FlyingGrab: return "#7dff5a";
                case AbilityType.ZeroGGrab: return "#9a6bff";
                case AbilityType.NoxiousGas: return "#e070c0";
                case AbilityType.Swarm: return "#5f6bff";
                case AbilityType.Charge: return "#9dff5a";
                case AbilityType.Lunge: return "#ffee44";
                case AbilityType.ItemThrow: return "#e0a030";
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
