using HarmonyLib;

namespace HideAndSeekMonster
{
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
    internal static class PlayerHealthHurtPatch
    {
        // Monster is invincible during a round (if enabled).
        private static bool Prefix(PlayerHealth __instance)
        {
            var mgr = HideAndSeekManager.Instance;
            if (mgr == null || !mgr.RoundActive || !mgr.SeekerInvincibleThisRound) return true;
            if (mgr.Seeker == null || __instance == null) return true;
            return __instance != mgr.Seeker.playerHealth;
        }
    }
}
