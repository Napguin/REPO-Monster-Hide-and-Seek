using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndSeekMonster
{
    public enum UiLanguage
    {
        English,
        Deutsch
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HideAndSeekMonster";
        public const string Name = "Hide And Seek Monster";
        public const string Version = "1.5.38";

        internal static ManualLogSource Log;

        // ---- Config ----
        internal static ConfigEntry<Key> MenuKey;
        internal static ConfigEntry<UiLanguage> Language;
        internal static ConfigEntry<float> HideTime;
        internal static ConfigEntry<float> RoundTime;
        internal static ConfigEntry<int> AttackDamage;
        internal static ConfigEntry<float> SeekerSpeed;
        internal static ConfigEntry<bool> SeekerInfiniteStamina;
        internal static ConfigEntry<bool> SeekerInvincible;
        internal static ConfigEntry<bool> BlindSeekerWhileHiding;
        internal static ConfigEntry<bool> DisableEnemies;
        internal static ConfigEntry<float> MonsterScale;
        internal static ConfigEntry<float> AttackRange;
        internal static ConfigEntry<float> AttackCooldown;
        internal static ConfigEntry<float> RoarCooldown;
        internal static ConfigEntry<bool> ThirdPerson;
        internal static ConfigEntry<float> CameraDistance;
        internal static ConfigEntry<string> AttackBinding;
        internal static ConfigEntry<string> RoarBinding;
        internal static ConfigEntry<string> ToggleViewBinding;
        internal static ConfigEntry<bool> TestMode;
        internal static ConfigEntry<string> SpecialBinding;
        internal static ConfigEntry<float> LaserDuration;
        internal static ConfigEntry<bool> MonsterCanGrab;
        internal static ConfigEntry<bool> SeekerChoosesMonster;
        internal static ConfigEntry<int> TestDummyCount;
        internal static ConfigEntry<bool> AfterRound;
        internal static ConfigEntry<int> MapLevel;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            MenuKey = Config.Bind("General", "MenuKey", Key.F8,
                "Key that opens the Hide & Seek menu (host only can start rounds).");
            Language = Config.Bind("General", "Language", UiLanguage.English,
                "Language of the in-game texts.");

            HideTime = Config.Bind("Round", "HideTime", 30f,
                new ConfigDescription("Seconds the hiders get to hide (seeker is blind & frozen).",
                    new AcceptableValueRange<float>(5f, 300f)));
            RoundTime = Config.Bind("Round", "RoundTime", 300f,
                new ConfigDescription("Seconds the seeker has to catch everyone after the hiding phase.",
                    new AcceptableValueRange<float>(30f, 1800f)));
            DisableEnemies = Config.Bind("Round", "DisableEnemies", true,
                "Despawn the normal enemies while a round is running (host setting).");

            SeekerSpeed = Config.Bind("Seeker", "SpeedMultiplier", 1.35f,
                new ConfigDescription("Movement speed multiplier of the monster.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            SeekerInfiniteStamina = Config.Bind("Seeker", "InfiniteStamina", true,
                "Monster never runs out of stamina.");
            SeekerInvincible = Config.Bind("Seeker", "Invincible", true,
                "Monster can not take damage during a round.");
            BlindSeekerWhileHiding = Config.Bind("Seeker", "BlindWhileHiding", true,
                "Black screen for the seeker during the hiding phase.");
            MonsterScale = Config.Bind("Seeker", "MonsterScale", 1f,
                new ConfigDescription("Size of the monster model.",
                    new AcceptableValueRange<float>(0.5f, 2f)));

            AttackRange = Config.Bind("Seeker", "AttackRange", 2.8f,
                new ConfigDescription("Reach of the monster's attack in metres.", new AcceptableValueRange<float>(1f, 6f)));
            AttackCooldown = Config.Bind("Seeker", "AttackCooldown", 1.2f,
                new ConfigDescription("Seconds between attacks.", new AcceptableValueRange<float>(0.2f, 10f)));
            AttackDamage = Config.Bind("Seeker", "AttackDamage", 40,
                new ConfigDescription("Damage of the monster's main attack (players have 100 HP).", new AcceptableValueRange<int>(5, 200)));
            RoarCooldown = Config.Bind("Seeker", "ScanCooldown", 20f,
                new ConfigDescription("Seconds between player scans (roar).", new AcceptableValueRange<float>(1f, 120f)));
            ThirdPerson = Config.Bind("Seeker", "ThirdPerson", true,
                "Monster plays in third person so you see your monster body (toggle in-game).");
            CameraDistance = Config.Bind("Seeker", "CameraDistance", 3.2f,
                new ConfigDescription("Third-person camera distance.", new AcceptableValueRange<float>(1f, 8f)));

            AttackBinding = Config.Bind("Controls", "Attack", "<Mouse>/leftButton",
                "Monster attack (Unity Input System path, e.g. <Mouse>/leftButton or <Keyboard>/f).");
            RoarBinding = Config.Bind("Controls", "Roar", "<Mouse>/rightButton",
                "Monster roar / player scan (Unity Input System path). R always works as well.");
            ToggleViewBinding = Config.Bind("Controls", "ToggleView", "<Keyboard>/v",
                "Switch the monster between third and first person (Unity Input System path).");

            SpecialBinding = Config.Bind("Controls", "Special", "<Mouse>/middleButton",
                "Special ability, e.g. the Clown's laser (Unity Input System path). F always works as well.");
            LaserDuration = Config.Bind("Seeker", "LaserDuration", 3f,
                new ConfigDescription("Seconds the laser fires.", new AcceptableValueRange<float>(0.5f, 10f)));
            MonsterCanGrab = Config.Bind("Seeker", "CanGrab", true,
                "Monster can grab things. Needed to open doors, press buttons etc. in R.E.P.O.");
            SeekerChoosesMonster = Config.Bind("Round", "SeekerChoosesMonster", true,
                "The seeker picks their own monster during the hiding phase (host setting).");

            AfterRound = Config.Bind("Round", "AfterRound", true,
                "After a round: show surviving hiders, revive + heal everyone, teleport all into the truck, then vote for the next seeker and the map (host setting).");
            MapLevel = Config.Bind("Round", "MapLevel", 0,
                new ConfigDescription("Level (map size) used when the host loads a new map from the Hide & Seek menu or the vote. 0 = keep the current level. Bigger level = bigger map (also moon phase).",
                    new AcceptableValueRange<int>(0, 20)));
            TestMode = Config.Bind("Test", "TestMode", false,
                "Solo test mode: start a round alone, training dummies act as hiders, debug overlay, F9 = next monster, F10 = spawn dummy, F11 = overlay.");
            TestDummyCount = Config.Bind("Test", "DummyCount", 4,
                new ConfigDescription("Number of training dummies spawned around you in test mode.", new AcceptableValueRange<int>(0, 20)));

            _harmony = new Harmony(Guid);
            // Patch each class separately: if a game update removed a target, only that feature is lost.
            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (System.Exception ex) { Log.LogWarning($"Patch {type.Name} skipped: {ex.Message}"); }
            }

            // Own persistent object, so we don't depend on the BepInEx manager object surviving.
            var go = new GameObject("HideAndSeekMonster_Manager");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<HideAndSeekManager>();

            Log.LogInfo($"{Name} {Version} loaded. Press {MenuKey.Value} in a level to open the menu.");
        }
    }
}
