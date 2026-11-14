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
