using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Item rules of the game mode (How To Play). Monster mode: firearms, melee weapons and cart cannons can't be
    /// grabbed during a round – grenades, mines, drones, orbs, health packs etc. stay allowed, and so do the tranq and
    /// pulse pistols. Every PC checks its own player: a banned item is let go right away.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private bool _roundBanWeapons;
        private float _banMsgAt;
        private readonly Dictionary<int, bool> _bannedCache = new Dictionary<int, bool>();

        /// <summary>True if this item may not be used in Monster mode.</summary>
        internal static bool IsBannedWeapon(Item item)
        {
            if (item == null) return false;
            string n = ((item.itemName ?? "") + " " + item.name).ToLowerInvariant();
            switch (item.itemType)
            {
                case SemiFunc.itemType.melee:
                    return true;
                case SemiFunc.itemType.launcher: // cart cannon
                    return true;
                case SemiFunc.itemType.gun:
                    return !(n.Contains("tranq") || n.Contains("pulse")); // tranq and pulse pistol do no damage
                default:
                    return false;
            }
        }

        private bool IsBannedObject(PhysGrabObject o)
        {
            if (o == null) return false;
            int key = o.GetInstanceID();
            if (_bannedCache.TryGetValue(key, out bool b)) return b;
            var attr = o.GetComponent<ItemAttributes>() ?? o.GetComponentInParent<ItemAttributes>();
            b = attr != null && IsBannedWeapon(attr.item);
            _bannedCache[key] = b;
            return b;
        }

        /// <summary>Per frame on every PC: let go of a banned weapon the local player grabbed.</summary>
        private void WeaponBanTick()
        {
            if (!RoundActive || !_roundBanWeapons) return;
            var grabber = Compat.Static("PhysGrabber", "instance") as PhysGrabber;
            if (grabber == null) return;
            var held = grabber.grabbedPhysGrabObject;
            if (held == null || !IsBannedObject(held)) return;
            var pv = held.GetComponent<Photon.Pun.PhotonView>();
            Compat.Call(grabber, "ReleaseObject", pv != null ? pv.ViewID : -1, 0.5f);
            Compat.Set(grabber, "grabDisableTimer", 0.3f);
            if (Time.time >= _banMsgAt)
            {
                _banMsgAt = Time.time + 2f;
                Banner(L.WeaponBanned, new Color(1f, 0.4f, 0.3f), 1.5f);
                Plugin.Log.LogInfo($"[Rules] banned weapon released: {held.name}");
            }
        }
    }
}

namespace HideAndSeekMonster
{
    /// <summary>
    /// Free item per round (How To Play, bonus shop rules): during the hiding phase every hider may take ONE item for
    /// free (I key opens the picker). The host checks it (allowed for hiders, once per round) and spawns it in front of
    /// that player. Cart cannons, scouts and banned weapons are not on the list; of the weapons, hiders only get
    /// things that do no damage (stun / pulse grenades and mines, tranq and pulse pistol).
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgFreeItem = 26;
        private bool _freePickOpen, _freeItemTaken;
        private int _freePickSel;
        private Vector2 _freePickScroll;
        private System.Collections.Generic.List<string> _freeItemList;
        private readonly System.Collections.Generic.HashSet<int> _hostFreeItemGiven = new System.Collections.Generic.HashSet<int>();

        internal static bool AllowedFreeItemForHider(Item it)
        {
            if (it == null || it.disabled || it.prefab == null || !it.prefab.IsValid()) return false;
            if (IsBannedWeapon(it)) return false;
            string n = ((it.itemName ?? "") + " " + it.name).ToLowerInvariant();
            if (n.Contains("scout")) return false;                      // scouts are banned
            switch (it.itemType)
            {
                case SemiFunc.itemType.cart:
                case SemiFunc.itemType.power_crystal:
                case SemiFunc.itemType.vehicle:
                    return false;
                case SemiFunc.itemType.grenade:
                case SemiFunc.itemType.mine:
                    // hiders may only use weapons that do no damage
                    return !(n.Contains("explosive") || n.Contains("duct") || n.Contains("human"));
                default:
                    return true;
            }
        }

        private System.Collections.Generic.List<string> FreeItemList()
        {
            if (_freeItemList != null && _freeItemList.Count > 0) return _freeItemList;
            _freeItemList = new System.Collections.Generic.List<string>();
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null) return _freeItemList;
            foreach (var kv in sm.itemDictionary)
                if (AllowedFreeItemForHider(kv.Value)) _freeItemList.Add(kv.Key);
            _freeItemList.Sort((a, b) => string.Compare(ItemLabel(a), ItemLabel(b), System.StringComparison.OrdinalIgnoreCase));
            return _freeItemList;
        }

        private static string ItemLabel(string key)
        {
            var sm = StatsManager.instance;
            if (sm != null && sm.itemDictionary != null && sm.itemDictionary.TryGetValue(key, out var it) && it != null && !string.IsNullOrEmpty(it.itemName))
                return it.itemName;
            return key.Replace("Item ", "");
        }

        private bool _roundFreeItem;
        private bool CanPickFreeItem => _roundFreeItem && RoundActive && Phase == Phase.Hiding && !_freeItemTaken && LocalHider(out _);

        /// <summary>Per frame: I opens / closes the picker while hiding.</summary>
        private void FreeItemKeys()
        {
            if (!CanPickFreeItem) { _freePickOpen = false; return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb[UnityEngine.InputSystem.Key.I].wasPressedThisFrame && !GameRefs.IsTyping) _freePickOpen = !_freePickOpen;
        }

        private void HostGiveFreeItem(int actor, string key)
        {
            if (!IsHost || !RoundActive || Phase != Phase.Hiding || !_roundFreeItem) return;
            var who = AvatarOfActor(actor);
            if (who == null || !IsAlive(who) || !_hiderViewIds.Contains(who.photonView.ViewID)) return;
            if (!_hostFreeItemGiven.Add(actor)) { Plugin.Log.LogInfo($"[FreeItem] {NameOf(who)} already took one"); return; }
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null || !sm.itemDictionary.TryGetValue(key, out var it) || !AllowedFreeItemForHider(it))
            {
                _hostFreeItemGiven.Remove(actor);
                Plugin.Log.LogInfo($"[FreeItem] '{key}' not allowed");
                return;
            }
            Vector3 f = who.transform.forward; f.y = 0; if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            SpawnPrefabAt(ItemLabel(key), it.prefab, PlayerPos(who) + f.normalized * 1.2f + Vector3.up * 1f);
            Plugin.Log.LogInfo($"[FreeItem] {NameOf(who)} gets '{key}'");
        }

        private void DrawFreeItemPicker(float sw, float sh, float u)
        {
            if (!_freePickOpen || !CanPickFreeItem) return;
            EnsureMenuStyles(u);
            var list = FreeItemList();
            float w = Mathf.Min(520 * u, sw - 40 * u), h = Mathf.Min(640 * u, sh - 80 * u);
            var r = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
            GUI.DrawTexture(r, Tex(new Color(0.05f, 0.07f, 0.1f, 0.97f)));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4 * u), Tex(new Color(0.3f, 0.55f, 1f, 1f)));
            GUI.Label(new Rect(r.x + 16 * u, r.y + 10 * u, r.width - 32 * u, 32 * u), L.FreeItemTitle, _mSection);
            GUI.Label(new Rect(r.x + 16 * u, r.y + 42 * u, r.width - 32 * u, 40 * u), L.FreeItemHint, _mSmall);
            var area = new Rect(r.x + 16 * u, r.y + 88 * u, r.width - 32 * u, r.height - 150 * u);
            float rowH = 38 * u, g = 5 * u;
            var content = new Rect(0, 0, area.width - 18 * u, Mathf.Max(1, list.Count) * (rowH + g));
            _freePickScroll = GUI.BeginScrollView(area, _freePickScroll, content);
            try
            {
                if (list.Count == 0) GUI.Label(new Rect(0, 0, content.width, rowH), L.DebugNothingFound, _mSmall);
                for (int i = 0; i < list.Count; i++)
                    if (GUI.Button(new Rect(0, i * (rowH + g), content.width, rowH), ItemLabel(list[i]), i == _freePickSel ? _cardSelStyle : _cardStyle))
                        _freePickSel = i;
            }
            finally { GUI.EndScrollView(); }
            float bw = (r.width - 48 * u) / 2f, by = r.yMax - 52 * u;
            GUI.enabled = list.Count > 0;
            if (GUI.Button(new Rect(r.x + 16 * u, by, bw, 40 * u), L.FreeItemTake, _chBtnMain) && list.Count > 0)
            {
                Send(MsgFreeItem, list[Mathf.Clamp(_freePickSel, 0, list.Count - 1)]);
                _freeItemTaken = true;
                _freePickOpen = false;
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(r.x + 32 * u + bw, by, bw, 40 * u), L.Close + "  (I)", _chBtn)) _freePickOpen = false;
        }

        // ---- tranq / pulse pistol: only 2 shots per round, no recharging (Monster mode with banned weapons) ----
        private static readonly System.Collections.Generic.Dictionary<int, int> _pistolShots = new System.Collections.Generic.Dictionary<int, int>();
        internal const int PistolShots = 2;

        internal static bool PistolRuleActive(Component c)
        {
            var mgr = Instance;
            if (mgr == null || !mgr.RoundActive || !mgr._roundBanWeapons || c == null) return false;
            var attr = c.GetComponent<ItemAttributes>() ?? c.GetComponentInParent<ItemAttributes>();
            return attr != null && attr.item != null && attr.item.itemType == SemiFunc.itemType.gun && !IsBannedWeapon(attr.item);
        }

        /// <summary>Called for every shot (on every PC). False = this pistol has no shots left.</summary>
        internal static bool PistolMayShoot(Component gun)
        {
            if (!PistolRuleActive(gun)) return true;
            int id = gun.GetInstanceID();
            _pistolShots.TryGetValue(id, out int n);
            if (n >= PistolShots) return false;
            _pistolShots[id] = n + 1;
            return true;
        }

        private static void ResetPistolShots() => _pistolShots.Clear();
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemGun), "ShootRPC")]
    internal static class PistolShotLimitPatch
    {
        private static bool Prefix(ItemGun __instance) => HideAndSeekManager.PistolMayShoot(__instance);
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemBattery), "ChargeBattery")]
    internal static class PistolNoRechargePatch
    {
        private static bool Prefix(ItemBattery __instance) => !HideAndSeekManager.PistolRuleActive(__instance);
    }
}
