using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace HideAndSeekMonster
{
    /// <summary>
    /// The laser cloned onto the monster brings the game's HurtCollider with it.
    /// Its damage is switched off here; hits are decided by the mod (host) instead,
    /// so hiders and test dummies are treated the same and nothing gets hit twice.
    /// </summary>
    internal static class MonsterHurtFilter
    {
        public static bool IsOurs(UnityEngine.Component c) =>
            c != null && c.GetComponentInParent<MonsterVisual>() != null;
    }

    [HarmonyPatch]
    internal static class MonsterHurtColliderBoolPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "CanHit", "EnemyHurt", "PhysObjectHurt", "VehicleImpactRuckusCanHit" })
            {
                var m = AccessTools.Method(typeof(HurtCollider), name);
                if (m != null && m.ReturnType == typeof(bool)) yield return m;
            }
        }

        private static bool Prefix(HurtCollider __instance, ref bool __result)
        {
            if (!MonsterHurtFilter.IsOurs(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MonsterHurtColliderVoidPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "PlayerHurt", "EnemyHurtRigidbody" })
            {
                var m = AccessTools.Method(typeof(HurtCollider), name);
                if (m != null && m.ReturnType == typeof(void)) yield return m;
            }
        }

        private static bool Prefix(HurtCollider __instance) => !MonsterHurtFilter.IsOurs(__instance);
    }
}
