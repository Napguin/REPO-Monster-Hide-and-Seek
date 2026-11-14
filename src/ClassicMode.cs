using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace HideAndSeekMonster
{
    internal enum GameMode { Monster = 0, Classic = 1 }

    /// <summary>
    /// Classic mode (How To Play): the hunter stays a normal player and starts with a melee weapon of choice (picked
    /// while the others hide, I key). Melee weapons have infinite uses, guns can't be recharged.
    /// Item ownership (both modes): the host remembers who got which item. Hiders can't grab the hunter's melee
    /// weapons, can't fire the hunter's guns and can't put the hunter's weapons into their inventory. At the start of
    /// the next round every remembered item is brought back to its owner.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgOwner = 27;
        private bool _roundClassic;
        internal bool ClassicActive => RoundActive && _roundClassic;

        // item view id -> owner actor number (every PC)
        private static readonly Dictionary<int, int> _itemOwner = new Dictionary<int, int>();

        private void ApplyClassic(bool classic)
        {
            _roundClassic = classic;
            if (!classic) return;
            if (_monster != null) { _monster.Remove(); _monster = null; }
            _monsterName = "";
            _choiceOpen = false;
            SeekerInvincibleThisRound = false;
            _roundSpeed = 1f;
            _roundInfStamina = false;
            Later(1f, () => { if (CanPickStartWeapon) Banner(L.StartWeaponPress, new Color(1f, 0.5f, 0.4f), 5f); });
            Plugin.Log.LogInfo("[Classic] round in classic mode");
        }

        // ---- weapons parked in the truck (every PC) ----
        private const int MsgItemLock = 29;
        private static readonly HashSet<int> _lockedItems = new HashSet<int>();
        private void OnItemLock(int[] ids) { _lockedItems.Clear(); if (ids != null) foreach (var i in ids) _lockedItems.Add(i); }

        /// <summary>Weapons in the sense of the rules: guns (except tranq / pulse), melee, cart cannons, explosive grenades / mines.</summary>
        internal static bool IsWeaponItem(Item it)
        {
            if (it == null) return false;
            if (IsBannedWeapon(it)) return true;
            if (it.itemType == SemiFunc.itemType.grenade || it.itemType == SemiFunc.itemType.mine)
            {
                string n = ((it.itemName ?? "") + " " + it.name).ToLowerInvariant();
                return n.Contains("explosive") || n.Contains("duct") || n.Contains("human");
            }
            return false;
        }

        private int SeekerActor => Seeker != null ? ActorOf(Seeker) : -999;

        /// <summary>The local player is the hunter of a classic round and hasn't taken the start weapon yet.</summary>
        private bool CanPickStartWeapon => ClassicActive && Phase == Phase.Hiding && !_startWeaponTaken && Seeker != null && GameRefs.IsLocal(Seeker);

        internal static bool IsStartWeapon(Item it) =>
            it != null && !it.disabled && it.prefab != null && it.prefab.IsValid()
            && (it.itemType == SemiFunc.itemType.melee || it.itemType == SemiFunc.itemType.gun); // melee or a gun of choice

        // ---- ownership ----
        // host: item view id -> which item it was (to give it back if it's gone)
        private readonly Dictionary<int, string> _hostItemKey = new Dictionary<int, string>();

        private void HostRegisterOwner(GameObject go, int actor, string key)
        {
            if (go == null) return;
            var pv = go.GetComponent<PhotonView>() ?? go.GetComponentInChildren<PhotonView>();
            if (pv == null || pv.ViewID == 0) return;
            _hostItemKey[pv.ViewID] = key;
            Send(MsgOwner, pv.ViewID, actor);
        }

        private void OnOwner(int viewId, int actor) => _itemOwner[viewId] = actor;

        private static int OwnerOf(Component c)
        {
            if (c == null) return -1;
            var pv = c.GetComponent<PhotonView>() ?? c.GetComponentInParent<PhotonView>();
            return pv != null && _itemOwner.TryGetValue(pv.ViewID, out int a) ? a : -1;
        }

        /// <summary>True if this item belongs to the hunter of the running round and the local player is a hider.</summary>
        internal static bool IsHunterItemForLocalHider(Component c)
        {
            var mgr = Instance;
            if (mgr == null || !mgr.RoundActive || mgr.Seeker == null || GameRefs.IsLocal(mgr.Seeker)) return false;
            int owner = OwnerOf(c);
            return owner >= 0 && owner == mgr.SeekerActor;
        }

        private static Item ItemOf(Component c)
        {
            if (c == null) return null;
            var attr = c.GetComponent<ItemAttributes>() ?? c.GetComponentInParent<ItemAttributes>();
            return attr != null ? attr.item : null;
        }

        /// <summary>Per frame (every PC): hiders let go of the hunter's melee weapon.</summary>
        private void HunterMeleeTick()
        {
            if (!RoundActive) return;
            var grabber = Compat.Static("PhysGrabber", "instance") as PhysGrabber;
            var held = grabber != null ? grabber.grabbedPhysGrabObject : null;
            if (held == null) return;
            var it = ItemOf(held);
            var pv = held.GetComponent<PhotonView>();
            bool locked = pv != null && _lockedItems.Contains(pv.ViewID);           // a hider's weapon parked in the truck
            bool hiderMelee = it != null && it.itemType == SemiFunc.itemType.melee && LocalHider(out _)
                              && (_roundClassic || IsHunterItemForLocalHider(held)); // rules: hiders may not use melee
            if (!locked && !hiderMelee) return;
            Compat.Call(grabber, "ReleaseObject", pv != null ? pv.ViewID : -1, 0.5f);
            Compat.Set(grabber, "grabDisableTimer", 0.3f);
            if (Time.time >= _banMsgAt) { _banMsgAt = Time.time + 2f; Banner(locked ? L.ItemLockedInTruck : L.HunterMeleeNo, new Color(1f, 0.4f, 0.3f), 1.5f); }
        }

        /// <summary>Host, at round start: every remembered item goes back to its owner (in front of them).</summary>
        private void HostReturnOwnedItems()
        {
            int n = 0, respawned = 0;
            var locked = new List<int>();
            foreach (var kv in _itemOwner.ToList())
            {
                var pv = PhotonView.Find(kv.Key);
                var owner = AvatarOfActor(kv.Value);
                if (owner == null) continue; // owner left – keep it for later
                if (pv == null)
                {
                    // gone (left lying in the old level, used up, broken ...): the owner gets a new one
                    _itemOwner.Remove(kv.Key);
                    if (!_hostItemKey.TryGetValue(kv.Key, out string key)) continue;
                    _hostItemKey.Remove(kv.Key);
                    var sm = StatsManager.instance;
                    if (sm == null || sm.itemDictionary == null || !sm.itemDictionary.TryGetValue(key, out var it) || it?.prefab == null) continue;
                    Vector3 fw = owner.transform.forward; fw.y = 0; if (fw.sqrMagnitude < 0.01f) fw = Vector3.forward;
                    var go = SpawnPrefabAt(ItemLabel(key), it.prefab, PlayerPos(owner) + fw.normalized * 1.2f + Vector3.up * (1f + 0.3f * n), true);
                    HostRegisterOwner(go, kv.Value, key);
                    n++; respawned++;
                    continue;
                }
                Vector3 f = owner.transform.forward; f.y = 0; if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                Vector3 pos = PlayerPos(owner) + f.normalized * 1.2f + Vector3.up * (1f + 0.3f * n);
                // rules: weapons owned by hiders stay in the truck this round and can't be used by anyone
                if (owner != Seeker && IsWeaponItem(ItemOf(pv)))
                {
                    pos = TruckPosition() + Quaternion.Euler(0, locked.Count * 40f, 0) * Vector3.forward * 1.2f + Vector3.up * (1f + 0.2f * locked.Count);
                    locked.Add(kv.Key);
                }
                var rb = pv.GetComponent<Rigidbody>();
                if (rb != null) { rb.position = pos; rb.velocity = Vector3.zero; }
                pv.transform.position = pos;
                n++;
            }
            Send(MsgItemLock, locked.ToArray());
            if (n > 0) Plugin.Log.LogInfo($"[Items] {n} item(s) returned to their owners ({respawned} spawned new, {locked.Count} hider weapon(s) parked in the truck)");
        }
    }

    // ---- Classic: melee weapons never lose durability ----
    [HarmonyLib.HarmonyPatch(typeof(ItemMelee), "SwingHitRPC")]
    internal static class ClassicMeleeDurabilityPatch
    {
        private static void Prefix(ref bool _durabilityLoss)
        {
            var mgr = HideAndSeekManager.Instance;
            if (mgr != null && mgr.ClassicActive) _durabilityLoss = false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemBattery), "Drain")]
    internal static class ClassicMeleeDrainPatch
    {
        private static bool Prefix(ItemBattery __instance)
        {
            var mgr = HideAndSeekManager.Instance;
            if (mgr == null || !mgr.ClassicActive) return true;
            var attr = __instance.GetComponent<ItemAttributes>() ?? __instance.GetComponentInParent<ItemAttributes>();
            return attr == null || attr.item == null || attr.item.itemType != SemiFunc.itemType.melee;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemBattery), "RemoveFullBar")]
    internal static class ClassicMeleeBarPatch
    {
        private static bool Prefix(ItemBattery __instance) => ClassicMeleeDrainPatch_Allow(__instance);
        internal static bool ClassicMeleeDrainPatch_Allow(ItemBattery b)
        {
            var mgr = HideAndSeekManager.Instance;
            if (mgr == null || !mgr.ClassicActive) return true;
            var attr = b.GetComponent<ItemAttributes>() ?? b.GetComponentInParent<ItemAttributes>();
            return attr == null || attr.item == null || attr.item.itemType != SemiFunc.itemType.melee;
        }
    }

    // ---- hiders can't put the hunter's weapons into their inventory ----
    [HarmonyLib.HarmonyPatch(typeof(ItemEquippable), "RequestEquip")]
    internal static class HunterItemEquipPatch
    {
        private static bool Prefix(ItemEquippable __instance)
        {
            if (!HideAndSeekManager.IsHunterItemForLocalHider(__instance)) return true;
            HideAndSeekManager.Instance?.BannerPublic(L.HunterItemNoInventory);
            return false;
        }
    }
}
