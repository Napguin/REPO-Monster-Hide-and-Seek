using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// The picker lists are built from the game's item list. If a player's own list is empty (seen once: "Nothing found"
    /// in the picker), the lists the host sends at every round start are used instead.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgItemLists = 32;

        private static List<string> _hostHider = new List<string>(), _hostMonster = new List<string>(),
            _hostHunter = new List<string>(), _hostWeapons = new List<string>();
        private static readonly Dictionary<string, string> _hostLabel = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _hostType = new Dictionary<string, int>();
        private bool _emptyListLogged;

        private void HostSendItemLists()
        {
            var hider = FreeItemList().ToArray();
            var monster = MonsterItemList().ToArray();
            var hunter = HunterExtraList().ToArray();
            var weapons = StartWeaponList().ToArray();
            var all = hider.Concat(monster).Concat(hunter).Concat(weapons).Distinct().ToArray();
            if (all.Length == 0) return;
            var labels = all.Select(ItemLabel).ToArray();
            var types = all.Select(k => { var t = ItemTypeOf(k); return t.HasValue ? (int)t.Value : -1; }).ToArray();
            Send(MsgItemLists, hider, monster, hunter, weapons, all, labels, types);
        }

        private void OnItemLists(string[] hider, string[] monster, string[] hunter, string[] weapons, string[] all, string[] labels, int[] types)
        {
            _hostHider = hider.ToList(); _hostMonster = monster.ToList(); _hostHunter = hunter.ToList(); _hostWeapons = weapons.ToList();
            for (int i = 0; i < all.Length; i++)
            {
                if (i < labels.Length) _hostLabel[all[i]] = labels[i];
                if (i < types.Length) _hostType[all[i]] = types[i];
            }
        }

        /// <summary>The own list, or the host's when the own one is empty.</summary>
        private List<string> OrHost(List<string> own, List<string> host, string which)
        {
            if (own.Count > 0) return own;
            if (!_emptyListLogged)
            {
                _emptyListLogged = true;
                var sm = StatsManager.instance;
                int n = sm != null && sm.itemDictionary != null ? sm.itemDictionary.Count : -1;
                int usable = sm != null && sm.itemDictionary != null ? sm.itemDictionary.Values.Count(Usable) : -1;
                Plugin.Log.LogWarning($"[Picker] own '{which}' list is empty (game items: {n}, usable: {usable}) – using the host's list ({host.Count})");
            }
            return host;
        }

        private static string HostLabel(string key) => _hostLabel.TryGetValue(key, out var l) ? l : null;
        private static SemiFunc.itemType? HostType(string key) => _hostType.TryGetValue(key, out var t) && t >= 0 ? (SemiFunc.itemType)t : (SemiFunc.itemType?)null;
    }
}
