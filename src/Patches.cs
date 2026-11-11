using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace HideAndSeekMonster
{
    /// <summary>
    /// "Monster invincible": R.E.P.O. can hurt / kill a player on several paths
    /// (local Hurt, HurtOther RPC, Death, PlayerDeath RPC, falling into pits ...).
    /// All of them are blocked for the seeker while a round runs with invincibility on.
    /// </summary>
    internal static class SeekerProtection
    {
        public static bool Protects(PlayerAvatar avatar)
        {
            var mgr = HideAndSeekManager.Instance;
            return mgr != null && mgr.RoundActive && mgr.SeekerInvincibleThisRound
                   && avatar != null && mgr.Seeker != null && avatar == mgr.Seeker;
        }

        public static bool Protects(PlayerHealth health) =>
            health != null && Protects(Compat.Get<PlayerAvatar>(health, "playerAvatar"));
    }

    [HarmonyPatch]
    internal static class SeekerHealthPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(PlayerHealth).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (m.Name == "Hurt" || m.Name == "HurtOther" || m.Name == "HurtOtherRPC" || m.Name == "Death")
                    yield return m;
        }

        private static bool Prefix(PlayerHealth __instance) => !SeekerProtection.Protects(__instance);
    }

    [HarmonyPatch]
    internal static class SeekerDeathPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(PlayerAvatar).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (m.Name == "PlayerDeath" || m.Name == "PlayerDeathRPC")
                    yield return m;
        }

        private static bool Prefix(PlayerAvatar __instance)
        {
            if (!SeekerProtection.Protects(__instance)) return true;
            Plugin.Log.LogInfo("[Monster] death of the invincible monster blocked");
            return false;
        }
    }
}

namespace HideAndSeekMonster
{
    /// <summary>The monster may not use the map (Tab) during a round – it has to find the hiders itself.</summary>
    internal static class SeekerMapBlock
    {
        public static bool Blocked
        {
            get
            {
                var mgr = HideAndSeekManager.Instance;
                return mgr != null && mgr.RoundActive && mgr.Seeker != null && GameRefs.IsLocal(mgr.Seeker);
            }
        }
    }

    [HarmonyLib.HarmonyPatch]
    internal static class SeekerMapInputPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var names = new[] { "KeyDown", "KeyUp", "KeyHold", "InputToggleGet" };
            foreach (var m in typeof(InputManager).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                if (System.Array.IndexOf(names, m.Name) >= 0 && m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(InputKey))
                    yield return m;
            foreach (var m in typeof(SemiFunc).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                if ((m.Name == "InputDown" || m.Name == "InputUp" || m.Name == "InputHold") && m.ReturnType == typeof(bool)
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(InputKey))
                    yield return m;
        }

        private static bool Prefix(InputKey key, ref bool __result)
        {
            if (key != InputKey.Map || !SeekerMapBlock.Blocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(MapToolController), "Update")]
    internal static class SeekerMapClosePatch
    {
        // If the map was open when the round started, close it.
        private static void Prefix(MapToolController __instance)
        {
            if (!SeekerMapBlock.Blocked || __instance == null) return;
            var owner = Compat.Get<PlayerAvatar>(__instance, "PlayerAvatar");
            if (owner != null && !GameRefs.IsLocal(owner)) return;
            Compat.Set(__instance, "mapToggled", false);
            Compat.Set(__instance, "Active", false);
        }
    }
}

namespace HideAndSeekMonster
{
    /// <summary>Swarm minions (real game enemies) and other enemies don't see or target the monster player.</summary>
    internal static class SeekerEnemyIgnore
    {
        public static bool IsSeeker(PlayerAvatar p)
        {
            var mgr = HideAndSeekManager.Instance;
            return p != null && mgr != null && mgr.RoundActive && mgr.Seeker == p;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(EnemyVision), "VisionTrigger")]
    internal static class SeekerVisionPatch
    {
        private static bool Prefix(PlayerAvatar player) => !SeekerEnemyIgnore.IsSeeker(player);
    }

    [HarmonyLib.HarmonyPatch]
    internal static class SeekerGroupTargetPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(EnemyGnomeDirector), typeof(EnemyBangDirector) })
            {
                var m = HarmonyLib.AccessTools.Method(t, "SetTarget", new[] { typeof(PlayerAvatar) });
                if (m != null) yield return m;
            }
        }

        private static bool Prefix(PlayerAvatar _player) => !SeekerEnemyIgnore.IsSeeker(_player);
    }
}
