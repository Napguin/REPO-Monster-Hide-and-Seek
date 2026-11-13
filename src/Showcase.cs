using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// The drawn animations of the Monster Showcase. They come from pictures/monster-showcase in the repository,
    /// are turned into single PNG frames by tools/make_showcase.py and embedded into the DLL (folder showcase/).
    /// Only the animation that is shown right now is loaded; the previous one is freed.
    /// </summary>
    internal static class ShowcaseArt
    {
        private static Dictionary<string, int[]> _index;
        private static string _loadedKey;
        private static Texture2D[] _frames;

        private static Dictionary<string, int[]> Index
        {
            get
            {
                if (_index != null) return _index;
                _index = new Dictionary<string, int[]>();
                var text = ReadText("showcase/index.txt");
                if (text == null) { Plugin.Log.LogWarning("[Showcase] index.txt missing in the DLL"); return _index; }
                foreach (var line in text.Split('\n'))
                {
                    var parts = line.Trim().Split('|');
                    if (parts.Length < 3) continue;
                    _index[parts[0]] = parts[2].Split(',').Select(s => int.TryParse(s, out int d) ? d : 100).ToArray();
                }
                return _index;
            }
        }

        public static bool Has(string key) => key != null && Index.ContainsKey(key);

        private static Stream Open(string name) => typeof(ShowcaseArt).Assembly.GetManifestResourceStream(name);

        private static string ReadText(string name)
        {
            using (var s = Open(name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        /// <summary>The frame to show now (animations loop), or null if there is no drawing for this key.</summary>
        public static Texture2D Frame(string key, float time)
        {
            if (!Has(key)) return null;
            if (_loadedKey != key) Load(key);
            if (_frames == null || _frames.Length == 0) return null;
            var durs = Index[key];
            if (_frames.Length == 1) return _frames[0];
            int total = durs.Sum();
            if (total <= 0) return _frames[0];
            int t = (int)(time * 1000f) % total;
            for (int i = 0; i < _frames.Length && i < durs.Length; i++)
            {
                if (t < durs[i]) return _frames[i];
                t -= durs[i];
            }
            return _frames[_frames.Length - 1];
        }

        private static void Load(string key)
        {
            Unload();
            _loadedKey = key;
            int n = Index[key].Length;
            var list = new List<Texture2D>();
            for (int i = 0; i < n; i++)
            {
                byte[] bytes;
                using (var s = Open($"showcase/{key}_{i}.png"))
                {
                    if (s == null) continue;
                    bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length) { int k = s.Read(bytes, read, bytes.Length - read); if (k <= 0) break; read += k; }
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(tex, bytes, false)) { Object.Destroy(tex); continue; }
                try { tex.Compress(false); tex.Apply(false, true); } catch { /* keep uncompressed */ }
                list.Add(tex);
            }
            _frames = list.ToArray();
            Plugin.Log.LogInfo($"[Showcase] '{key}': {_frames.Length} frame(s) loaded");
        }

        public static void Unload()
        {
            if (_frames != null) foreach (var t in _frames) if (t != null) Object.Destroy(t);
            _frames = null;
            _loadedKey = null;
        }
    }

    public partial class HideAndSeekManager
    {
        private enum MenuPage { Main, Showcase, HowToPlay }
        private MenuPage _menuPage = MenuPage.Main;
        private int _showcaseSel;
        private float _showcaseStart;
        private Vector2 _showcaseScroll, _showcaseInfoScroll;

        /// <summary>One entry of the showcase: a drawing (key) and what it shows.</summary>
        private class ShowcaseEntry
        {
            public string Key;            // drawing in showcase/ (null = no drawing yet)
            public AbilityType Ability;   // None = the main attack / basics
            public string OnlyMonster;    // e.g. Swarm: separate drawings for Gnome and Banger
        }

        private static readonly ShowcaseEntry[] ShowcaseEntries =
        {
            new ShowcaseEntry { Key = "attack", Ability = AbilityType.None },
            new ShowcaseEntry { Key = "clown", Ability = AbilityType.Laser },
            new ShowcaseEntry { Key = "huntsman", Ability = AbilityType.Shot },
            new ShowcaseEntry { Key = "sprint", Ability = AbilityType.Sprint },
            new ShowcaseEntry { Key = "duck", Ability = AbilityType.FlyingCharge },
            new ShowcaseEntry { Key = "charge", Ability = AbilityType.Charge },
            new ShowcaseEntry { Key = "lunge", Ability = AbilityType.Lunge },
            new ShowcaseEntry { Key = "xray", Ability = AbilityType.Reveal },
            new ShowcaseEntry { Key = "oogly", Ability = AbilityType.FlyingGrab },
            new ShowcaseEntry { Key = "mentalist", Ability = AbilityType.ZeroGGrab },
            new ShowcaseEntry { Key = "cleaning_crew", Ability = AbilityType.Bomb },
            new ShowcaseEntry { Key = "spewer", Ability = AbilityType.Puke },
            new ShowcaseEntry { Key = "noxious_gas", Ability = AbilityType.NoxiousGas },
            new ShowcaseEntry { Key = "gnomes", Ability = AbilityType.Swarm, OnlyMonster = "Gnome" },
            new ShowcaseEntry { Key = "bangers", Ability = AbilityType.Swarm, OnlyMonster = "Banger" },
            new ShowcaseEntry { Key = "rugrat", Ability = AbilityType.ItemThrow },
            new ShowcaseEntry { Key = "bowtie", Ability = AbilityType.Tornado },
            new ShowcaseEntry { Key = "pony", Ability = AbilityType.Lasso },
            new ShowcaseEntry { Key = "upscream", Ability = AbilityType.Launch },
            new ShowcaseEntry { Key = "trudge", Ability = AbilityType.Impulse },
            new ShowcaseEntry { Key = null, Ability = AbilityType.BalloonMines },
            new ShowcaseEntry { Key = null, Ability = AbilityType.TumbleBash },
            new ShowcaseEntry { Key = null, Ability = AbilityType.Gamble },
        };

        private static string ShowcaseTitle(ShowcaseEntry e) =>
            e.Ability == AbilityType.None ? L.ShowcaseBasics : Abilities.Name(e.Ability) + (e.OnlyMonster != null ? $" ({e.OnlyMonster})" : "");

        private static string ShowcaseMonsters(ShowcaseEntry e)
        {
            if (e.Ability == AbilityType.None) return L.AllMonsters;
            if (e.OnlyMonster != null) return e.OnlyMonster;
            var names = MonsterCatalog.Entries.Where(m => Abilities.For(m) == e.Ability).Select(m => m.Name).ToList();
            return names.Count > 0 ? string.Join(", ", names) : "–";
        }

        /// <summary>The two buttons next to "Close" in the menu header.</summary>
        private void DrawMenuPageButtons(Rect panel, float pad, float u)
        {
            float bw = 210 * u, bh = 38 * u, y = panel.y + 18 * u;
            float x = panel.xMax - pad - 120 * u - 10 * u - bw * 2 - 10 * u;
            if (GUI.Button(new Rect(x, y, bw, bh), _menuPage == MenuPage.Showcase ? "◀ " + L.Back : L.MonsterShowcase, _chBtn))
                SetMenuPage(_menuPage == MenuPage.Showcase ? MenuPage.Main : MenuPage.Showcase);
            if (GUI.Button(new Rect(x + bw + 10 * u, y, bw, bh), _menuPage == MenuPage.HowToPlay ? "◀ " + L.Back : L.HowToPlay, _chBtn))
                SetMenuPage(_menuPage == MenuPage.HowToPlay ? MenuPage.Main : MenuPage.HowToPlay);
        }

        private void SetMenuPage(MenuPage p)
        {
            _menuPage = p;
            _showcaseStart = Time.realtimeSinceStartup;
            if (p != MenuPage.Showcase) ShowcaseArt.Unload();
        }

        private void DrawShowcase(Rect body, float u)
        {
            float gap = 16 * u;
            var list = new Rect(body.x, body.y, body.width * 0.26f, body.height);
            var right = new Rect(list.xMax + gap, body.y, body.width - list.width - gap, body.height);
            GUI.DrawTexture(list, Tex(new Color(0.10f, 0.075f, 0.08f, 1f)));
            GUI.DrawTexture(right, Tex(new Color(0.10f, 0.075f, 0.08f, 1f)));

            // ---- list ----
            float p = 10 * u, rowH = 44 * u, rg = 6 * u;
            var area = new Rect(list.x + p, list.y + p, list.width - 2 * p, list.height - 2 * p);
            var content = new Rect(0, 0, area.width - 18 * u, ShowcaseEntries.Length * (rowH + rg));
            _showcaseScroll = GUI.BeginScrollView(area, _showcaseScroll, content);
            try
            {
                for (int i = 0; i < ShowcaseEntries.Length; i++)
                {
                    var e = ShowcaseEntries[i];
                    string col = e.Ability == AbilityType.None ? "#ff6b5a" : Abilities.Color(e.Ability);
                    string label = $"<b><color={col}>{ShowcaseTitle(e)}</color></b>";
                    if (GUI.Button(new Rect(0, i * (rowH + rg), content.width, rowH), label, i == _showcaseSel ? _cardSelStyle : _cardStyle) && i != _showcaseSel)
                    {
                        _showcaseSel = i;
                        _showcaseStart = Time.realtimeSinceStartup;
                        _showcaseInfoScroll = Vector2.zero;
                    }
                }
            }
            finally { GUI.EndScrollView(); }

            // ---- drawing + info ----
            var sel = ShowcaseEntries[Mathf.Clamp(_showcaseSel, 0, ShowcaseEntries.Length - 1)];
            float infoH = 150 * u;
            float size = Mathf.Min(right.width - 2 * p, right.height - infoH - 3 * p);
            var img = new Rect(right.x + (right.width - size) / 2f, right.y + p, size, size);
            GUI.DrawTexture(img, Tex(Color.black));
            var frame = ShowcaseArt.Frame(sel.Key, Time.realtimeSinceStartup - _showcaseStart);
            if (frame != null) GUI.DrawTexture(img, frame, ScaleMode.ScaleToFit);
            else
            {
                GUI.color = new Color(1, 1, 1, 0.6f);
                GUI.Label(new Rect(img.x, img.center.y - 20 * u, img.width, 40 * u), L.DrawingSoon, CenterStyle(u));
                GUI.color = Color.white;
            }

            var info = new Rect(right.x + p, img.yMax + p, right.width - 2 * p, right.yMax - img.yMax - 2 * p);
            string head;
            string desc;
            if (sel.Ability == AbilityType.None)
            {
                head = $"<b><color=#ff6b5a>{L.ShowcaseBasics}</color></b>   ·   {L.AllMonsters}";
                desc = L.ShowcaseBasicsText;
            }
            else
            {
                head = $"<b><color={Abilities.Color(sel.Ability)}>{Abilities.Name(sel.Ability)}</color></b>   ·   {L.CooldownLabel}: {Abilities.Cooldown(sel.Ability):0} s   ·   {L.MonstersLabel}: {ShowcaseMonsters(sel)}";
                desc = Abilities.Description(sel.Ability);
            }
            var infoContent = new Rect(0, 0, info.width - 18 * u, 260 * u);
            _showcaseInfoScroll = GUI.BeginScrollView(info, _showcaseInfoScroll, infoContent);
            try
            {
                GUI.Label(new Rect(0, 0, infoContent.width, 30 * u), head, _mSmall);
                GUI.Label(new Rect(0, 32 * u, infoContent.width, 220 * u), desc, _mSmall);
            }
            finally { GUI.EndScrollView(); }
        }

        private GUIStyle _mCenter;
        private GUIStyle CenterStyle(float u)
        {
            if (_mCenter == null) _mCenter = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            _mCenter.fontSize = Mathf.RoundToInt(22 * u);
            return _mCenter;
        }

        private void DrawHowToPlay(Rect body, float u)
        {
            GUI.DrawTexture(body, Tex(new Color(0.10f, 0.075f, 0.08f, 1f)));
            GUI.color = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(body.x, body.center.y - 30 * u, body.width, 60 * u), L.HowToPlaySoon, CenterStyle(u));
            GUI.color = Color.white;
        }
    }
}
