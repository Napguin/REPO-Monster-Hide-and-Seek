using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Animation tuner (opened from the F7 debug menu): pick for every role of the current monster which clip
    /// plays, which part of it and how fast. Preview it on the model and save it to
    /// BepInEx/config/HideAndSeekMonster.animations.cfg (that file can be sent in to become the default).
    /// Only changes this PC.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private bool _animTunerOpen;
        private int _tuneRole, _tuneClip = -1;
        private float _tuneA, _tuneB = 1f, _tuneSpeed = 1f;
        private bool _tuneLoop;
        private string _tuneLoadedFor;
        private string _tuneMsg = "";

        // Text that doesn't fit gets a smaller font instead of being cut off.
        private GUIStyle _fitBtnStyle, _fitLabelStyle;
        private static int FitSize(GUIStyle st, string text, float width, int max)
        {
            int size = max;
            var gc = new GUIContent(text);
            for (; size > 8; size--)
            {
                st.fontSize = size;
                if (st.CalcSize(gc).x <= width) break;
            }
            return size;
        }

        private bool FitButton(Rect r, string text)
        {
            if (_fitBtnStyle == null) _fitBtnStyle = new GUIStyle(_chBtn);
            _fitBtnStyle.normal = _chBtn.normal; _fitBtnStyle.hover = _chBtn.hover; _fitBtnStyle.active = _chBtn.active;
            FitSize(_fitBtnStyle, text, r.width - 10f, _chBtn.fontSize);
            return GUI.Button(r, text, _fitBtnStyle);
        }

        private void FitLabel(Rect r, string text, GUIStyle st)
        {
            if (_fitLabelStyle == null) _fitLabelStyle = new GUIStyle(st);
            _fitLabelStyle.alignment = st.alignment; _fitLabelStyle.richText = st.richText; _fitLabelStyle.wordWrap = false;
            FitSize(_fitLabelStyle, text, r.width - 6f, st.fontSize);
            GUI.Label(r, text, _fitLabelStyle);
        }

        private void LoadTuneRole(MonsterVisual mv)
        {
            string role = AnimProfiles.Roles[_tuneRole];
            var t = mv.Clips.TrackOf(role);
            var st = t?.Steps.FirstOrDefault();
            _tuneClip = st != null ? mv.Clips.Clips.IndexOf(st.Clip) : -1;
            _tuneA = st?.A ?? 0f;
            _tuneB = st?.B ?? 1f;
            _tuneSpeed = st?.Speed ?? 1f;
            _tuneLoop = t != null && t.Loop;
            _tuneLoadedFor = mv.MonsterName + "/" + role;
        }

        private string TuneSpec(MonsterVisual mv)
        {
            if (_tuneClip < 0 || _tuneClip >= mv.Clips.Clips.Count) return "-";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return mv.Clips.Clips[_tuneClip].name
                   + "@" + _tuneA.ToString("0.00", inv) + "-" + _tuneB.ToString("0.00", inv)
                   + "*" + _tuneSpeed.ToString("0.00", inv) + (_tuneLoop ? "~" : "");
        }

        private void DrawAnimTuner(float sw, float sh, float u)
        {
            EnsureMenuStyles(u);
            float w = Mathf.Min(560 * u, sw * 0.45f), h = Mathf.Min(sh - 40 * u, 700 * u);
            var r = new Rect(20 * u, (sh - h) / 2f, w, h);
            GUI.DrawTexture(r, Tex(new Color(0.06f, 0.06f, 0.08f, 0.97f)));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4 * u), Tex(new Color(0.9f, 0.6f, 0.2f, 1f)));
            GUI.Label(new Rect(r.x + 16 * u, r.y + 10 * u, r.width - 140 * u, 36 * u), "ANIMATION TUNER", _mSection);
            if (FitButton(new Rect(r.xMax - 116 * u, r.y + 10 * u, 100 * u, 32 * u), L.Close)) _animTunerOpen = false;

            var area = new Rect(r.x + 16 * u, r.y + 54 * u, r.width - 32 * u, r.height - 64 * u);
            _tuneScroll = GUI.BeginScrollView(area, _tuneScroll, new Rect(0, 0, area.width - 18 * u, 640 * u));
            try { DrawAnimTunerBody(area.width - 18 * u, u, r.height); }
            finally { GUI.EndScrollView(); }
        }

        private Vector2 _tuneScroll;

        private void DrawAnimTunerBody(float cw, float u, float panelH)
        {
            float x = 0, y = 0, bh = 34 * u, g = 6 * u;
            var r = new Rect(0, 0, cw, 640 * u);
            var mv = _monster;
            if (mv == null || mv.Clips == null)
            {
                GUI.Label(new Rect(x, y, cw, 120 * u),
                    mv == null ? "No monster right now. Start a round (alone is fine) and open this again."
                               : $"'{mv.MonsterName}' has no animation clips the tuner can use (the game animates it by code).", _mSmall);
                y += 130 * u;
                if (FitButton(new Rect(x, y, cw, bh), "Dump all monster clips to the log")) { AnimProfiles.DumpAll(); _tuneMsg = "Written to LogOutput.log"; }
                y += bh + g;
                GUI.Label(new Rect(x, y, cw, 40 * u), _tuneMsg, _mSmall);
                return;
            }

            var clips = mv.Clips.Clips;
            string role = AnimProfiles.Roles[_tuneRole];
            if (_tuneLoadedFor != mv.MonsterName + "/" + role) LoadTuneRole(mv);

            FitLabel(new Rect(x, y, cw, 28 * u), $"<b>{mv.MonsterName}</b>   playing: {mv.Clips.Current}", _mSmall);
            y += 32 * u;

            // role
            if (FitButton(new Rect(x, y, 44 * u, bh), "◀")) { _tuneRole = (_tuneRole - 1 + AnimProfiles.Roles.Length) % AnimProfiles.Roles.Length; LoadTuneRole(mv); }
            FitLabel(new Rect(x + 50 * u, y, cw - 100 * u, bh), $"Role: <b>{AnimProfiles.Roles[_tuneRole]}</b>", CenterStyleSmall(u));
            if (FitButton(new Rect(x + cw - 44 * u, y, 44 * u, bh), "▶")) { _tuneRole = (_tuneRole + 1) % AnimProfiles.Roles.Length; LoadTuneRole(mv); }
            y += bh + g;
            role = AnimProfiles.Roles[_tuneRole];
            mv.Profile.Specs.TryGetValue(role, out var spec);
            GUI.color = new Color(1, 1, 1, 0.6f);
            GUI.Label(new Rect(x, y, cw, 44 * u), "now: " + (string.IsNullOrEmpty(spec) ? "(none)" : spec), _mSmall);
            GUI.color = Color.white;
            y += 46 * u;

            // clip
            int n = clips.Count;
            if (FitButton(new Rect(x, y, 44 * u, bh), "◀")) _tuneClip = _tuneClip <= -1 ? n - 1 : _tuneClip - 1;
            string cname = _tuneClip >= 0 && _tuneClip < n ? $"{clips[_tuneClip].name} ({clips[_tuneClip].length:0.00}s)" : "(no clip)";
            FitLabel(new Rect(x + 50 * u, y, cw - 100 * u, bh), cname, CenterStyleSmall(u));
            if (FitButton(new Rect(x + cw - 44 * u, y, 44 * u, bh), "▶")) _tuneClip = _tuneClip >= n - 1 ? -1 : _tuneClip + 1;
            y += bh + g;

            void Slider(string label, ref float v, float min, float max)
            {
                FitLabel(new Rect(x, y, 120 * u, bh), $"{label} {v:0.00}", _mSmall);
                v = GUI.HorizontalSlider(new Rect(x + 126 * u, y + bh * 0.4f, cw - 130 * u, bh), v, min, max);
                y += bh;
            }
            Slider("Start", ref _tuneA, 0f, 1f);
            Slider("End", ref _tuneB, 0f, 1f);
            Slider("Speed", ref _tuneSpeed, 0f, 2.5f);
            if (_tuneB <= _tuneA) _tuneB = Mathf.Min(1f, _tuneA + 0.05f);
            _tuneLoop = GUI.Toggle(new Rect(x, y, cw, bh), _tuneLoop, "  Loop (for charge / laser / puke)");
            y += bh + g;

            float third = (cw - 2 * g) / 3f;
            if (FitButton(new Rect(x, y, third, bh), "Preview"))
            {
                var t = AnimProfiles.Parse(role, TuneSpec(mv), clips);
                if (t != null) { t.Loop = false; mv.Clips.Preview(t); }
            }
            if (FitButton(new Rect(x + third + g, y, third, bh), "Save"))
            {
                string s = TuneSpec(mv);
                mv.Profile.Specs[role] = s;
                if (s == "-") mv.Profile.Specs.Remove(role);
                AnimProfiles.SaveUser(mv.MonsterName, role, s);
                mv.Clips.Rebuild();
                _tuneMsg = $"Saved {role} for {mv.MonsterName}.";
            }
            if (FitButton(new Rect(x + 2 * (third + g), y, third, bh), "Reset"))
            {
                AnimProfiles.SaveUser(mv.MonsterName, role, "");
                var fresh = AnimProfiles.For(mv.MonsterName);
                mv.Profile.Specs.Clear();
                foreach (var kv in fresh.Specs) mv.Profile.Specs[kv.Key] = kv.Value;
                mv.Clips.Rebuild();
                LoadTuneRole(mv);
                _tuneMsg = $"{role} back to the built-in setting.";
            }
            y += bh + g;
            if (FitButton(new Rect(x, y, cw, bh), "Dump all monster clips to the log")) { AnimProfiles.DumpAll(); _tuneMsg = "Written to LogOutput.log"; }
            y += bh + g;
            GUI.Label(new Rect(x, y, cw, 30 * u), _tuneMsg, _mSmall);
            y += 30 * u;
            GUI.color = new Color(1, 1, 1, 0.6f);
            GUI.Label(new Rect(x, y, cw, 90 * u), "Only this PC. Saved in BepInEx/config/HideAndSeekMonster.animations.cfg – send that file in to make it the default for everyone.", _mSmall);
            GUI.color = Color.white;
        }
    }
}
