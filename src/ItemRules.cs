using System.Linq;
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
        private readonly System.Collections.Generic.HashSet<int> _hostStartWeaponGiven = new System.Collections.Generic.HashSet<int>();

        // ---- who may pick which item (Flyguy's item gallery: blue = hiders, red = hunter, white = both) ----
        internal enum ItemRole { None, Both, Hiders, Hunter }

        internal static ItemRole RoleOf(Item it)
        {
            if (it == null || it.prefab == null || !it.prefab.IsValid()) return ItemRole.None;
            string n = ((it.itemName ?? "") + " " + it.name).ToLowerInvariant();
            if (n.Contains("scout")) return ItemRole.None;                 // not in the gallery
            switch (it.itemType)
            {
                case SemiFunc.itemType.power_crystal:
                case SemiFunc.itemType.vehicle:
                case SemiFunc.itemType.launcher:                            // cart cannons are banned
                    return ItemRole.None;
                case SemiFunc.itemType.cart:
                    return n.Contains("small") || n.Contains("pocket") ? ItemRole.Both : ItemRole.None; // only the POCKET C.A.R.T.
                case SemiFunc.itemType.melee:
                    return ItemRole.Hunter;                                 // f-pan, sword, prod-z, sledge, bat, hammer, whip
                case SemiFunc.itemType.gun:
                    // tranq + pulse pistol: hiders; every other gun: hunter
                    return n.Contains("tranq") || n.Contains("shockwave") || n.Contains("pulse") ? ItemRole.Hiders : ItemRole.Hunter;
            }
            // no-damage things: hiders (stun grenade / mine, pulse grenade / mine, leaf blower)
            if (n.Contains("stun") || n.Contains("shockwave") || n.Contains("pulse") || n.Contains("leaf") || n.Contains("blower"))
                return ItemRole.Hiders;
            // damage: hunter (grenade, duct-tape grenade, human grenade, explosive mine, zap trap)
            if (n.Contains("explosive") || n.Contains("duct") || n.Contains("human") || n.Contains("trap") || n.Contains("zap"))
                return ItemRole.Hunter;
            return ItemRole.Both; // duck bucket, walkie talkie, bridge, cart, med-kit, defibrillator, orbs, drones, staffs, upgrades
        }

        private static bool Usable(Item it) => it != null && !it.disabled && it.prefab != null && it.prefab.IsValid();
        private static bool IsGunOrMelee(Item it) => it.itemType == SemiFunc.itemType.gun || it.itemType == SemiFunc.itemType.melee;

        /// <summary>Hiders (both modes): their own items and the shared ones.</summary>
        internal static bool AllowedFreeItemForHider(Item it)
        {
            if (!Usable(it)) return false;
            var r = RoleOf(it);
            return r == ItemRole.Hiders || r == ItemRole.Both;
        }

        /// <summary>Monster mode hunter: the hunter's items and the shared ones – no guns or melee weapons.</summary>
        internal static bool AllowedForMonster(Item it)
        {
            if (!Usable(it) || (IsGunOrMelee(it) && RoleOf(it) == ItemRole.Hunter)) return false;
            var r = RoleOf(it);
            return r == ItemRole.Hunter || r == ItemRole.Both;
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
            LogItemRoles();
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
        private bool _startWeaponTaken;
        private bool CanPickFreeItem => _roundFreeItem && RoundActive && Phase == Phase.Hiding && !_freeItemTaken
            && (LocalHider(out _)
                || (Seeker != null && GameRefs.IsLocal(Seeker) && (!_roundClassic || (ClassicActive && _startWeaponTaken)))); // the monster gets an item too

        /// <summary>Per frame: I opens / closes the picker while hiding.</summary>
        private bool PickerAvailable => CanPickFreeItem || CanPickStartWeapon;

        /// <summary>Classic hunter's second item: anything except cart cannons and scouts.</summary>
        private System.Collections.Generic.List<string> _hunterExtraList;
        private System.Collections.Generic.List<string> HunterExtraList()
        {
            if (_hunterExtraList != null && _hunterExtraList.Count > 0) return _hunterExtraList;
            _hunterExtraList = new System.Collections.Generic.List<string>();
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null) return _hunterExtraList;
            foreach (var kv in sm.itemDictionary)
                if (AllowedHunterExtra(kv.Value)) _hunterExtraList.Add(kv.Key);
            _hunterExtraList.Sort((a, b) => string.Compare(ItemLabel(a), ItemLabel(b), System.StringComparison.OrdinalIgnoreCase));
            return _hunterExtraList;
        }

        /// <summary>Classic hunter's second item: the hunter's items (all weapons) and the shared ones.</summary>
        internal static bool AllowedHunterExtra(Item it)
        {
            if (!Usable(it)) return false;
            var r = RoleOf(it);
            return r == ItemRole.Hunter || r == ItemRole.Both;
        }

        private System.Collections.Generic.List<string> _monsterItemList;
        private System.Collections.Generic.List<string> MonsterItemList()
        {
            if (_monsterItemList != null && _monsterItemList.Count > 0) return _monsterItemList;
            _monsterItemList = new System.Collections.Generic.List<string>();
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null) return _monsterItemList;
            foreach (var kv in sm.itemDictionary)
                if (AllowedForMonster(kv.Value)) _monsterItemList.Add(kv.Key);
            _monsterItemList.Sort((a, b) => string.Compare(ItemLabel(a), ItemLabel(b), System.StringComparison.OrdinalIgnoreCase));
            LogItemRoles();
            return _monsterItemList;
        }

        private static bool _rolesLogged;
        /// <summary>Once: every item of the game with its role (to compare with the item gallery).</summary>
        private static void LogItemRoles()
        {
            if (_rolesLogged) return;
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null || sm.itemDictionary.Count == 0) return;
            _rolesLogged = true;
            var sb = new System.Text.StringBuilder("[Items] roles (item – type – role):\n");
            foreach (var kv in sm.itemDictionary.OrderBy(k => k.Key))
            {
                var it = kv.Value;
                string extra = it == null ? " (null)" : it.disabled ? " (disabled in the game)" : (it.prefab == null || !it.prefab.IsValid()) ? " (no prefab)" : "";
                sb.Append("  ").Append(kv.Key).Append(" – ").Append(it != null ? it.itemType.ToString() : "?").Append(" – ").Append(RoleOf(it)).Append(extra).Append('\n');
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private System.Collections.Generic.List<string> _startWeaponList;
        private System.Collections.Generic.List<string> StartWeaponList()
        {
            if (_startWeaponList != null && _startWeaponList.Count > 0) return _startWeaponList;
            _startWeaponList = new System.Collections.Generic.List<string>();
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null) return _startWeaponList;
            foreach (var kv in sm.itemDictionary) if (IsStartWeapon(kv.Value)) _startWeaponList.Add(kv.Key);
            _startWeaponList.Sort((a, b) => string.Compare(ItemLabel(a), ItemLabel(b), System.StringComparison.OrdinalIgnoreCase));
            return _startWeaponList;
        }

        private float _iconScanAt;

        /// <summary>Every few seconds: remember the icons of items that exist in the world (shop, truck, inventory).
        /// The game only creates an item's icon when the item is spawned, so this fills the picker over time.</summary>
        private void HarvestItemIcons()
        {
            if (Time.realtimeSinceStartup < _iconScanAt) return;
            _iconScanAt = Time.realtimeSinceStartup + 5f;
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null) return;
            foreach (var a in Object.FindObjectsOfType<ItemAttributes>())
            {
                if (a == null || a.icon == null || a.item == null) continue;
                foreach (var kv in sm.itemDictionary)
                    if (kv.Value == a.item) { _itemIcons[kv.Key] = a.icon; break; }
            }
        }

        private void FreeItemKeys()
        {
            HarvestItemIcons();
            if (!PickerAvailable) { _freePickOpen = false; return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb[UnityEngine.InputSystem.Key.I].wasPressedThisFrame && !GameRefs.IsTyping) { _freePickOpen = !_freePickOpen; if (_freePickOpen) _freePickSel = -1; }
        }

        private void HostGiveFreeItem(int actor, string key)
        {
            if (!IsHost || !RoundActive || Phase != Phase.Hiding || !_roundFreeItem) return;
            var who = AvatarOfActor(actor);
            if (who == null || !IsAlive(who)) return;
            bool hunter = who == Seeker;
            if (!hunter && !_hiderViewIds.Contains(who.photonView.ViewID)) return;
            bool startWeapon = hunter && _roundClassic && !_hostStartWeaponGiven.Contains(actor); // classic hunter: weapon first
            bool classicHunter = hunter && _roundClassic;
            if (!startWeapon && !_hostFreeItemGiven.Add(actor)) { Plugin.Log.LogInfo($"[FreeItem] {NameOf(who)} already took one"); return; }
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null || !sm.itemDictionary.TryGetValue(key, out var it)
                || !(startWeapon ? IsStartWeapon(it) : classicHunter ? AllowedHunterExtra(it) : hunter ? AllowedForMonster(it) : AllowedFreeItemForHider(it)))
            {
                if (!startWeapon) _hostFreeItemGiven.Remove(actor);
                Plugin.Log.LogInfo($"[FreeItem] '{key}' not allowed");
                return;
            }
            if (startWeapon) _hostStartWeaponGiven.Add(actor);
            Vector3 f = who.transform.forward; f.y = 0; if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            var go = SpawnPrefabAt(ItemLabel(key), it.prefab, PlayerPos(who) + f.normalized * 1.2f + Vector3.up * 1f, true); // no message for the host
            HostRegisterOwner(go, actor, key);
            Plugin.Log.LogInfo($"[FreeItem] {NameOf(who)} gets '{key}'");
        }

        // ---- picker window: item icons in a grid, grouped by category ----
        private readonly System.Collections.Generic.Dictionary<string, Sprite> _itemIcons = new System.Collections.Generic.Dictionary<string, Sprite>();

        private bool _iconLogged;
        private readonly System.Collections.Generic.Dictionary<string, float> _iconRetryAt = new System.Collections.Generic.Dictionary<string, float>();

        private Sprite ItemIcon(string key)
        {
            if (_itemIcons.TryGetValue(key, out var sp)) return sp;
            if (_iconRetryAt.TryGetValue(key, out float retry) && Time.realtimeSinceStartup < retry) return null;
            _iconRetryAt[key] = Time.realtimeSinceStartup + 3f; // look again in 3 s (not every GUI call)
            sp = null;
            string why = "";
            try
            {
                var sm = StatsManager.instance;
                Item it = null;
                if (sm != null && sm.itemDictionary != null) sm.itemDictionary.TryGetValue(key, out it);
                if (it == null) why = "item not found";
                else
                {
                    // 1) the item's prefab
                    GameObject prefab = null;
                    try { prefab = it.prefab?.Prefab; } catch (System.Exception ex) { why += "prefab: " + ex.Message + "; "; }
                    // 2) loaded by its resource path
                    if (prefab == null && it.prefab != null && !string.IsNullOrEmpty(it.prefab.ResourcePath))
                        prefab = Resources.Load<GameObject>(it.prefab.ResourcePath);
                    var attr = prefab != null ? prefab.GetComponentInChildren<ItemAttributes>(true) : null;
                    if (attr != null) sp = attr.icon;
                    if (sp == null) why += prefab == null ? "no prefab; " : attr == null ? "no ItemAttributes; " : "icon empty on prefab; ";
                    // 3) a copy of that item somewhere in the scene (shop, truck, inventory) – set up at runtime
                    if (sp == null)
                        foreach (var a in Object.FindObjectsOfType<ItemAttributes>())
                            if (a != null && a.item == it && a.icon != null) { sp = a.icon; break; }
                    if (sp == null) why += "none in scene";
                }
            }
            catch (System.Exception ex) { why += ex.Message; }
            if (sp == null && !_iconLogged) { _iconLogged = true; Plugin.Log.LogInfo($"[Picker] no icon for '{key}': {why}"); }
            // only cache hits – a missing icon is looked for again a bit later (items in the scene can appear)
            if (sp != null) _itemIcons[key] = sp;
            return sp;
        }

        // Fallback: the game's item emojis (TextMeshPro sprite sheet used in its text UI), by Item.emojiIcon name.
        private static System.Collections.Generic.Dictionary<string, (Texture tex, Rect px)> _emoji;
        private static readonly System.Collections.Generic.HashSet<string> _emojiMissLogged = new System.Collections.Generic.HashSet<string>();

        private static bool EmojiFor(string key, out Texture tex, out Rect px)
        {
            tex = null; px = default;
            if (_emoji == null)
            {
                _emoji = new System.Collections.Generic.Dictionary<string, (Texture, Rect)>(System.StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var sa in Resources.FindObjectsOfTypeAll<TMPro.TMP_SpriteAsset>())
                    {
                        if (sa == null || sa.spriteSheet == null || sa.spriteCharacterTable == null) continue;
                        foreach (var ch in sa.spriteCharacterTable)
                        {
                            if (ch == null || ch.glyph == null || string.IsNullOrEmpty(ch.name)) continue;
                            var gr = ch.glyph.glyphRect;
                            _emoji[ch.name] = (sa.spriteSheet, new Rect(gr.x, gr.y, gr.width, gr.height));
                        }
                    }
                    Plugin.Log.LogInfo($"[Picker] {_emoji.Count} item emojis found");
                }
                catch (System.Exception ex) { Plugin.Log.LogInfo("[Picker] emoji lookup failed: " + ex.Message); }
            }
            var sm = StatsManager.instance;
            if (sm == null || sm.itemDictionary == null || !sm.itemDictionary.TryGetValue(key, out var it) || it == null) return false;
            bool own = _emoji.TryGetValue(it.emojiIcon.ToString(), out var e)
                       // an item without its own emoji has the default value (drone_heal) – only trust it for heal drones
                       && !(it.emojiIcon == default(SemiFunc.emojiIcon) && !(key.ToLowerInvariant().Contains("drone") && key.ToLowerInvariant().Contains("heal")));
            if (!own)
            {
                // guess by name (some items have no / a wrong emoji set, e.g. the mines)
                string k = key.ToLowerInvariant();
                string guess = null;
                if (k.Contains("mine")) guess = k.Contains("shockwave") ? "item_mine_shockwave" : k.Contains("explosive") ? "item_mine_explosive" : "item_mine_stun";
                else if (k.Contains("trapzap") || k.Contains("trap zap")) guess = "item_mine_stun";
                else if (k.Contains("grenade")) guess = k.Contains("shockwave") ? "item_grenade_shockwave" : k.Contains("stun") ? "grenade_stun" : k.Contains("duct") ? "item_grenade_duct_taped" : k.Contains("human") ? "item_grenade_human" : "grenade_explosive";
                else if (k.Contains("tracker")) guess = k.Contains("extraction") ? "item_extraction_tracker" : "item_valuable_tracker";
                else if (k.Contains("health")) guess = k.Contains("small") ? "item_health_pack_S" : k.Contains("large") || k.Contains("big") ? "item_health_pack_L" : "item_health_pack_M";
                if (guess == null || !_emoji.TryGetValue(guess, out e))
                {
                    // last try: an emoji whose name contains the item's main words (e.g. "mine" + "shockwave")
                    var words = k.Replace("item", " ").Split(new[] { ' ', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 2).ToArray();
                    string best = null;
                    foreach (var name in _emoji.Keys)
                    {
                        string nl = name.ToLowerInvariant();
                        if (words.Length > 0 && words.All(w => nl.Contains(w))) { best = name; break; }
                    }
                    if (best == null || !_emoji.TryGetValue(best, out e))
                    {
                        if (_emojiMissLogged.Add(key))
                            Plugin.Log.LogInfo($"[Picker] no emoji for '{key}' (own: {it.emojiIcon}); emojis with 'mine': " +
                                string.Join(", ", _emoji.Keys.Where(x => x.ToLowerInvariant().Contains("mine")).Take(12).ToArray()));
                        return false;
                    }
                }
            }
            tex = e.tex; px = e.px;
            return tex != null;
        }

        private static void DrawTexturePart(Rect r, Texture tex, Rect px)
        {
            var uv = new Rect(px.x / tex.width, px.y / tex.height, px.width / tex.width, px.height / tex.height);
            float aspect = px.width / Mathf.Max(1f, px.height);
            Rect d = r;
            if (aspect > 1f) { d.height = r.width / aspect; d.y += (r.height - d.height) / 2f; }
            else { d.width = r.height * aspect; d.x += (r.width - d.width) / 2f; }
            GUI.DrawTextureWithTexCoords(d, tex, uv);
        }

        private static SemiFunc.itemType? ItemTypeOf(string key)
        {
            var sm = StatsManager.instance;
            if (sm != null && sm.itemDictionary != null && sm.itemDictionary.TryGetValue(key, out var it) && it != null) return it.itemType;
            return null;
        }

        private static readonly SemiFunc.itemType[] CategoryOrder =
        {
            SemiFunc.itemType.melee, SemiFunc.itemType.gun, SemiFunc.itemType.grenade, SemiFunc.itemType.mine,
            SemiFunc.itemType.drone, SemiFunc.itemType.orb, SemiFunc.itemType.healthPack, SemiFunc.itemType.tool,
            SemiFunc.itemType.tracker, SemiFunc.itemType.pocket_cart, SemiFunc.itemType.player_upgrade, SemiFunc.itemType.item_upgrade,
        };

        private static void DrawSprite(Rect r, Sprite sp)
        {
            if (sp == null || sp.texture == null) return;
            var tr = sp.textureRect;
            var tex = sp.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            // keep the aspect ratio
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            Rect d = r;
            if (aspect > 1f) { d.height = r.width / aspect; d.y += (r.height - d.height) / 2f; }
            else { d.width = r.height * aspect; d.x += (r.width - d.width) / 2f; }
            GUI.DrawTextureWithTexCoords(d, tex, uv);
        }

        private GUIStyle _cellLabel;

        private void DrawFreeItemPicker(float sw, float sh, float u)
        {
            if (!_freePickOpen || !PickerAvailable) return;
            EnsureMenuStyles(u);
            if (_cellLabel == null) _cellLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, richText = true };
            _cellLabel.fontSize = Mathf.RoundToInt(13 * u);
            bool weapon = CanPickStartWeapon;
            bool hunterExtra = !weapon && _roundClassic && Seeker != null && GameRefs.IsLocal(Seeker); // the monster picks from the normal list
            bool monsterPick = !weapon && !_roundClassic && Seeker != null && GameRefs.IsLocal(Seeker);
            var list = weapon ? StartWeaponList() : hunterExtra ? HunterExtraList() : monsterPick ? MonsterItemList() : FreeItemList();
            if (_freePickSel >= list.Count) _freePickSel = -1;

            float w = Mathf.Min(900 * u, sw - 40 * u), h = Mathf.Min(760 * u, sh - 60 * u);
            var r = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
            GUI.DrawTexture(r, Tex(new Color(0.05f, 0.07f, 0.1f, 0.97f)));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4 * u), Tex(new Color(0.3f, 0.55f, 1f, 1f)));
            GUI.Label(new Rect(r.x + 16 * u, r.y + 10 * u, r.width - 32 * u, 32 * u), weapon ? L.StartWeaponTitle : L.FreeItemTitle, _mSection);
            GUI.Label(new Rect(r.x + 16 * u, r.y + 42 * u, r.width - 32 * u, 40 * u), weapon ? L.StartWeaponHint : L.FreeItemHint, _mSmall);

            // group by category
            var groups = new System.Collections.Generic.List<(string title, System.Collections.Generic.List<int> idx)>();
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (var cat in CategoryOrder)
            {
                var idx = new System.Collections.Generic.List<int>();
                for (int i = 0; i < list.Count; i++) if (ItemTypeOf(list[i]) == cat && used.Add(i)) idx.Add(i);
                if (idx.Count > 0) groups.Add((L.ItemCategory(cat), idx));
            }
            var rest = new System.Collections.Generic.List<int>();
            for (int i = 0; i < list.Count; i++) if (!used.Contains(i)) rest.Add(i);
            if (rest.Count > 0) groups.Add((L.ItemCategoryOther, rest));

            if (_freePickSel < 0 && groups.Count > 0) _freePickSel = groups[0].idx[0]; // first tile is selected at the start
            var area = new Rect(r.x + 16 * u, r.y + 88 * u, r.width - 32 * u, r.height - 150 * u);
            float cell = 104 * u, g = 8 * u, headH = 30 * u;
            int cols = Mathf.Max(1, Mathf.FloorToInt((area.width - 18 * u + g) / (cell + g)));
            float contentH = 0;
            foreach (var gr in groups) contentH += headH + Mathf.CeilToInt(gr.idx.Count / (float)cols) * (cell + 28 * u + g);
            var content = new Rect(0, 0, area.width - 18 * u, Mathf.Max(contentH, 10));
            _freePickScroll = UiUtil.VScroll(area, _freePickScroll, content);
            try
            {
                if (list.Count == 0) GUI.Label(new Rect(0, 0, content.width, 40 * u), L.DebugNothingFound, _mSmall);
                float y = 0;
                foreach (var gr in groups)
                {
                    GUI.Label(new Rect(0, y, content.width, headH), gr.title, _mSection);
                    y += headH;
                    for (int k = 0; k < gr.idx.Count; k++)
                    {
                        int i = gr.idx[k];
                        float x = (k % cols) * (cell + g);
                        float yy = y + (k / cols) * (cell + 28 * u + g);
                        var cr = new Rect(x, yy, cell, cell + 28 * u);
                        if (GUI.Button(cr, GUIContent.none, i == _freePickSel ? _cardSelStyle : _cardStyle)) _freePickSel = i;
                        var iconRect = new Rect(cr.x + 14 * u, cr.y + 6 * u, cell - 28 * u, cell - 28 * u);
                        var icon = ItemIcon(list[i]);
                        if (icon != null) DrawSprite(iconRect, icon);
                        else if (EmojiFor(list[i], out var etex, out var epx)) DrawTexturePart(iconRect, etex, epx);
                        GUI.Label(new Rect(cr.x + 2 * u, cr.y + cell - 18 * u, cell - 4 * u, 44 * u), ItemLabel(list[i]), _cellLabel);
                    }
                    y += Mathf.CeilToInt(gr.idx.Count / (float)cols) * (cell + 28 * u + g);
                }
            }
            finally { GUI.EndScrollView(); }

            float bw = (r.width - 48 * u) / 2f, by = r.yMax - 52 * u;
            GUI.enabled = list.Count > 0;
            string take = list.Count > 0 ? $"{L.FreeItemTake}: {ItemLabel(list[Mathf.Clamp(_freePickSel, 0, list.Count - 1)])}" : L.FreeItemTake;
            if (GUI.Button(new Rect(r.x + 16 * u, by, bw, 40 * u), take, _chBtnMain) && list.Count > 0)
            {
                Send(MsgFreeItem, list[Mathf.Clamp(_freePickSel, 0, list.Count - 1)]);
                if (weapon) { _startWeaponTaken = true; Banner(L.HunterExtraPress, new Color(1f, 0.5f, 0.4f), 3f); }
                else _freeItemTaken = true;
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
            if (mgr == null || !mgr.RoundActive || c == null) return false;
            var attr = c.GetComponent<ItemAttributes>() ?? c.GetComponentInParent<ItemAttributes>();
            return attr != null && attr.item != null && attr.item.itemType == SemiFunc.itemType.gun && !IsBannedWeapon(attr.item);
        }

        /// <summary>Who holds this item right now (else who held it last).</summary>
        internal static PlayerAvatar HolderOf(Component c)
        {
            var pgo = c == null ? null : c.GetComponent<PhysGrabObject>() ?? c.GetComponentInParent<PhysGrabObject>();
            if (pgo == null) return null;
            if (pgo.playerGrabbing != null)
                foreach (var g in pgo.playerGrabbing)
                    if (g != null && g.playerAvatar != null) return g.playerAvatar;
            return pgo.lastPlayerGrabbing;
        }

        private bool IsHiderAvatar(PlayerAvatar p) => p != null && p != Seeker && p.photonView != null && _hiderViewIds.Contains(p.photonView.ViewID);

        /// <summary>Called for every shot (on every PC). False = this pistol has no shots left.
        /// Rules: a hider gets only 2 shots per tranq / pulse pistol per round (both modes); the hunter has no limit.</summary>
        internal static bool PistolMayShoot(Component gun)
        {
            if (!PistolRuleActive(gun)) return true;
            var mgr = Instance;
            if (!mgr.IsHiderAvatar(HolderOf(gun))) return true;
            int id = gun.GetInstanceID();
            _pistolShots.TryGetValue(id, out int n);
            if (n >= PistolShots) return false;
            _pistolShots[id] = n + 1;
            return true;
        }

        private static void ResetPistolShots() => _pistolShots.Clear();

        /// <summary>Classic rules: hiders may grab / wrestle guns but only fire the tranq and pulse pistol.</summary>
        internal static bool HiderMayFire(Component gun, Photon.Pun.PhotonMessageInfo info)
        {
            var mgr = Instance;
            if (mgr == null || !mgr.ClassicActive || gun == null) return true;
            var attr = gun.GetComponent<ItemAttributes>() ?? gun.GetComponentInParent<ItemAttributes>();
            if (attr == null || !IsBannedWeapon(attr.item)) return true;
            // who fires = who holds it (the shot RPC itself is sent by the host, who runs the item physics)
            PlayerAvatar shooter = HolderOf(gun);
            if (shooter == null && info.Sender != null) shooter = mgr.AvatarOfActor(info.Sender.ActorNumber);
            if (shooter == null || shooter == mgr.Seeker || !mgr._hiderViewIds.Contains(shooter.photonView.ViewID)) return true;
            if (shooter.photonView.IsMine) mgr.BannerPublic(L.HiderNoFire);
            Plugin.Log.LogInfo($"[Rules] shot blocked: hider {NameOf(shooter)} holds '{attr.item.itemName}'");
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemGun), "ShootRPC")]
    internal static class PistolShotLimitPatch
    {
        private static bool Prefix(ItemGun __instance, Photon.Pun.PhotonMessageInfo _info)
        {
            if (!HideAndSeekManager.HiderMayFire(__instance, _info)) return false;
            return HideAndSeekManager.PistolMayShoot(__instance);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ItemBattery), "ChargeBattery")]
    internal static class PistolNoRechargePatch
    {
        private static bool Prefix(ItemBattery __instance)
        {
            if (HideAndSeekManager.PistolRuleActive(__instance)) return false;
            // Classic: guns have limited ammo – no recharging during a round
            var mgr = HideAndSeekManager.Instance;
            if (mgr != null && mgr.ClassicActive)
            {
                var attr = __instance.GetComponent<ItemAttributes>() ?? __instance.GetComponentInParent<ItemAttributes>();
                if (attr != null && attr.item != null && attr.item.itemType == SemiFunc.itemType.gun) return false;
            }
            return true;
        }
    }
}
