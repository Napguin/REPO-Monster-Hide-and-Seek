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
