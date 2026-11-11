using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>The F8 start menu (host: set up and start a round; everyone: scores).</summary>
    public partial class HideAndSeekManager
    {
        private Vector2 _menuScrollSeeker, _menuScrollMonster, _menuScrollRules;
        private GUIStyle _mHead, _mSection, _mSmall, _pillOn, _pillOff, _mValue;

        private void EnsureMenuStyles(float u)
        {
            EnsureChoiceStyles(u);
            if (_mHead == null)
            {
                _mHead = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _mSection = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _mSmall = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                _mValue = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
                _pillOn = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, richText = true };
                _pillOn.normal.background = Tex(new Color(0.16f, 0.32f, 0.18f, 1f));
                _pillOn.hover.background = Tex(new Color(0.20f, 0.40f, 0.22f, 1f));
                _pillOn.normal.textColor = _pillOn.hover.textColor = Color.white;
                _pillOff = new GUIStyle(_pillOn);
                _pillOff.normal.background = Tex(new Color(0.16f, 0.13f, 0.14f, 1f));
                _pillOff.hover.background = Tex(new Color(0.24f, 0.19f, 0.20f, 1f));
                _pillOff.normal.textColor = _pillOff.hover.textColor = new Color(0.8f, 0.75f, 0.76f);
            }
            _mHead.fontSize = Mathf.RoundToInt(32 * u);
            _mSection.fontSize = Mathf.RoundToInt(20 * u);
            _mSmall.fontSize = Mathf.RoundToInt(16 * u);
            _mValue.fontSize = Mathf.RoundToInt(16 * u);
            _pillOn.fontSize = _pillOff.fontSize = Mathf.RoundToInt(16 * u);
            _pillOn.padding = _pillOff.padding = new RectOffset((int)(12 * u), (int)(8 * u), (int)(6 * u), (int)(6 * u));
        }

        private void DrawStartMenu(float sw, float sh, float u)
        {
            EnsureMenuStyles(u);
            bool full = IsHost && !RoundActive && InLevel();

            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
            GUI.color = Color.white;

            float pw = full ? Mathf.Min(sw - 40 * u, 1400 * u) : Mathf.Min(sw - 40 * u, 640 * u);
            int scoreLines = Mathf.Max(1, _sbNames.Length) + (IsHost && _sbNames.Length > 0 ? 2 : 0);
            float simpleH = 74 * u + 40 * u + (RoundActive ? (IsHost ? 130 * u : 70 * u) : 40 * u) + 50 * u + scoreLines * 26 * u + 30 * u;
            float ph = full ? Mathf.Min(sh - 40 * u, 860 * u) : Mathf.Min(sh - 40 * u, simpleH);
            var panel = new Rect((sw - pw) / 2f, (sh - ph) / 2f, pw, ph);
            GUI.DrawTexture(panel, Tex(new Color(0.06f, 0.045f, 0.05f, 1f)));
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 4 * u), Tex(new Color(0.8f, 0.1f, 0.08f, 1f)));

            float pad = 24 * u;
            GUI.color = new Color(1f, 0.25f, 0.2f);
            GUI.Label(new Rect(panel.x + pad, panel.y + 12 * u, pw - 200 * u, 50 * u), "HIDE & SEEK · MONSTER", _mHead);
            GUI.color = Color.white;
            if (GUI.Button(new Rect(panel.xMax - pad - 120 * u, panel.y + 18 * u, 120 * u, 38 * u), L.Close + "  (F8)", _chBtn)) _menuOpen = false;

            var body = new Rect(panel.x + pad, panel.y + 74 * u, pw - 2 * pad, ph - 74 * u - pad);
            if (!full) { DrawSimpleMenu(body, u); return; }

            float gap = 16 * u;
            float c1 = body.width * 0.22f, c2 = body.width * 0.42f, c3 = body.width - c1 - c2 - 2 * gap;
            var col1 = new Rect(body.x, body.y, c1, body.height);
            var col2 = new Rect(col1.xMax + gap, body.y, c2, body.height);
            var col3 = new Rect(col2.xMax + gap, body.y, c3, body.height);
            foreach (var c in new[] { col1, col2, col3 }) GUI.DrawTexture(c, Tex(new Color(0.10f, 0.075f, 0.08f, 1f)));

            // Each column on its own: an error in one must not blank the whole menu.
            MenuPart("seeker column", () => DrawSeekerColumn(col1, u));
            MenuPart("monster column", () => DrawMonsterColumn(col2, u));
            MenuPart("rules column", () => DrawRulesColumn(col3, u));
        }

        private readonly System.Collections.Generic.Dictionary<string, float> _menuErrorAt = new System.Collections.Generic.Dictionary<string, float>();

        private void MenuPart(string part, System.Action draw)
        {
            try { draw(); }
            catch (System.Exception ex)
            {
                if (ex is ExitGUIException) throw;
                if (!_menuErrorAt.TryGetValue(part, out float t) || Time.realtimeSinceStartup - t > 5f)
                {
                    _menuErrorAt[part] = Time.realtimeSinceStartup;
                    Plugin.Log.LogError($"[menu: {part}] {ex}");
                    _lastError = $"menu {part}: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        // ---- 1. seeker ---------------------------------------------------
        private void DrawSeekerColumn(Rect r, float u)
        {
            float p = 14 * u;
            GUI.Label(new Rect(r.x + p, r.y + 8 * u, r.width, 32 * u), "1 · " + L.ChooseSeekerShort, _mSection);
            var alive = AllPlayers().Where(IsAlive).ToList();
            if (_selectedViewId >= 0 && !alive.Any(pl => pl.photonView.ViewID == _selectedViewId)) _selectedViewId = -1;

            var area = new Rect(r.x + p, r.y + 48 * u, r.width - 2 * p, r.height - 56 * u);
            float cardH = 50 * u, gap = 8 * u;
            var content = new Rect(0, 0, area.width - 18 * u, (alive.Count + 1) * (cardH + gap));
            _menuScrollSeeker = GUI.BeginScrollView(area, _menuScrollSeeker, content);
            try
            {
                float y = 0;
                if (GUI.Button(new Rect(0, y, content.width, cardH), $"<b>{L.Random}</b>", _selectedViewId < 0 ? _cardSelStyle : _cardStyle)) _selectedViewId = -1;
                y += cardH + gap;
                foreach (var pl in alive)
                {
                    int vid = pl.photonView.ViewID;
                    string me = GameRefs.IsLocal(pl) ? $"  <color=#a89a9c>({L.You})</color>" : "";
                    if (GUI.Button(new Rect(0, y, content.width, cardH), $"<b>{NameOf(pl)}</b>{me}", _selectedViewId == vid ? _cardSelStyle : _cardStyle))
                        _selectedViewId = vid;
                    y += cardH + gap;
                }
            }
            finally
            {
                GUI.EndScrollView();
            }
        }

        // ---- 2. monster --------------------------------------------------
        private void DrawMonsterColumn(Rect r, float u)
        {
            float p = 14 * u;
            GUI.Label(new Rect(r.x + p, r.y + 8 * u, r.width, 32 * u), "2 · " + L.ChooseMonster, _mSection);

            bool chooses = Plugin.SeekerChoosesMonster.Value;
            var pill = new Rect(r.x + p, r.y + 46 * u, r.width - 2 * p, 40 * u);
            if (GUI.Button(pill, (chooses ? $"<color=#7dff8a>[{L.On}]</color>  " : $"<color=#a89a9c>[{L.Off}]</color>  ") + L.SeekerChooses, chooses ? _pillOn : _pillOff))
                Plugin.SeekerChoosesMonster.Value = !chooses;
            GUI.color = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(r.x + p, pill.yMax + 4 * u, r.width - 2 * p, 44 * u), chooses ? L.StartMonsterHint : L.FixedMonsterHint, _mSmall);
            GUI.color = Color.white;

            var list = MonsterCatalog.Entries;
            var area = new Rect(r.x + p, pill.yMax + 54 * u, r.width - 2 * p, r.yMax - pill.yMax - 62 * u);
            int cols = 2;
            float gap = 8 * u, cardH = 58 * u;
            float cardW = (area.width - 18 * u - gap) / cols;
            int count = list.Count + 1;
            var content = new Rect(0, 0, area.width - 18 * u, Mathf.CeilToInt(count / (float)cols) * (cardH + gap));
            _menuScrollMonster = GUI.BeginScrollView(area, _menuScrollMonster, content);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var rc = new Rect((i % cols) * (cardW + gap), (i / cols) * (cardH + gap), cardW, cardH);
                    if (i == 0)
                    {
                        if (GUI.Button(rc, $"<b>{L.Random}</b>\n<size={Mathf.RoundToInt(14 * u)}><color=#a89a9c>{L.RandomMonsterHint}</color></size>",
                                _selectedMonster == RandomMonster ? _cardSelStyle : _cardStyle))
                            _selectedMonster = RandomMonster;
                        continue;
                    }
                    var e = list[i - 1];
                    var ab = e.Ability;
                    string text = $"<b>{e.Name}</b>\n<size={Mathf.RoundToInt(14 * u)}><color={Abilities.Color(ab)}>★ {Abilities.Name(ab)}</color></size>";
                    if (GUI.Button(rc, text, _selectedMonster == e.Name ? _cardSelStyle : _cardStyle)) _selectedMonster = e.Name;
                }
            }
            finally
            {
                GUI.EndScrollView();
            }
            if (list.Count == 0) GUI.Label(new Rect(area.x, area.y + 70 * u, area.width, 60 * u), L.NoMonsters, _mSmall);
        }

        // ---- 3. rules / scores / start -------------------------------------
        private void DrawRulesColumn(Rect r, float u)
        {
            float p = 14 * u;
            GUI.Label(new Rect(r.x + p, r.y + 8 * u, r.width, 32 * u), "3 · " + L.Rules, _mSection);

            float startH = 64 * u;
            var area = new Rect(r.x + p, r.y + 46 * u, r.width - 2 * p, r.height - 46 * u - startH - 2 * p);
            GUILayout.BeginArea(area);
            _menuScrollRules = GUILayout.BeginScrollView(_menuScrollRules);
            try
            {
                Plugin.HideTime.Value = MenuSlider(L.HideTime, Plugin.HideTime.Value, 5f, 120f, "s", 0, u);
                Plugin.RoundTime.Value = MenuSlider(L.RoundTime, Plugin.RoundTime.Value, 30f, 900f, "s", 0, u);
                Plugin.AttackRange.Value = MenuSlider(L.AttackRange, Plugin.AttackRange.Value, 1f, 5f, "m", 1, u);
                Plugin.SeekerSpeed.Value = MenuSlider(L.SeekerSpeed, Plugin.SeekerSpeed.Value, 0.5f, 2.5f, "x", 2, u);
                Plugin.MonsterScale.Value = MenuSlider(L.MonsterSize, Plugin.MonsterScale.Value, 0.5f, 2f, "x", 2, u);
                GUILayout.Space(6 * u);
                Plugin.SeekerInvincible.Value = MenuToggle(L.Invincible, Plugin.SeekerInvincible.Value, u);
                Plugin.DisableEnemies.Value = MenuToggle(L.DisableEnemies, Plugin.DisableEnemies.Value, u);
                Plugin.BlindSeekerWhileHiding.Value = MenuToggle(L.Blind, Plugin.BlindSeekerWhileHiding.Value, u);
                Plugin.MonsterCanGrab.Value = MenuToggle(L.CanGrab, Plugin.MonsterCanGrab.Value, u);
                Plugin.AfterRound.Value = MenuToggle(L.AfterRoundToggle, Plugin.AfterRound.Value, u);
                Plugin.TestMode.Value = MenuToggle(L.TestMode, Plugin.TestMode.Value, u);
                if (Plugin.TestMode.Value) GUILayout.Label($"<color=#a89a9c>{L.TestModeHint}</color>", _mSmall);

                GUILayout.Space(10 * u);
                GUILayout.Label(L.ScoreTitle, _mSection);
                if (_sbNames.Length == 0) GUILayout.Label($"<color=#a89a9c>{L.NoScores}</color>", _mSmall);
                for (int i = 0; i < _sbNames.Length; i++)
                    GUILayout.Label($"{i + 1}. {_sbNames[i]}  <b>{(i < _sbTotal.Length ? _sbTotal[i] : 0)}</b>", _mSmall);
                if (_sbNames.Length > 0 && GUILayout.Button(L.ResetScores, _chBtn, GUILayout.Height(34 * u))) HostResetScores();
            }
            finally
            {
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            var alive = AllPlayers().Where(IsAlive).ToList();
            bool enough = alive.Count >= 2 || !SemiFunc.IsMultiplayer() || Plugin.TestMode.Value;
            var startRect = new Rect(r.x + p, r.yMax - startH - p, r.width - 2 * p, startH);
            if (!enough)
            {
                GUI.color = new Color(1f, 0.6f, 0.5f);
                GUI.Label(new Rect(startRect.x, startRect.y - 26 * u, startRect.width, 24 * u), L.NotEnoughPlayers, _mSmall);
                GUI.color = Color.white;
            }
            GUI.enabled = enough;
            var startStyle = new GUIStyle(_chBtnMain) { fontSize = Mathf.RoundToInt(26 * u) };
            if (GUI.Button(startRect, L.Start.ToUpperInvariant(), startStyle)) HostStartRound();
            GUI.enabled = true;
        }

        private float MenuSlider(string label, float value, float min, float max, string unit, int decimals, float u)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _mSmall);
            GUILayout.Label(value.ToString("F" + decimals) + unit, _mValue, GUILayout.Width(70 * u));
            GUILayout.EndHorizontal();
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(18 * u));
            GUILayout.Space(6 * u);
            if (decimals == 0) value = Mathf.Round(value);
            return value;
        }

        private bool MenuToggle(string label, bool value, float u)
        {
            if (GUILayout.Button((value ? $"<color=#7dff8a>[{L.On}]</color>  " : $"<color=#a89a9c>[{L.Off}]</color>  ") + label, value ? _pillOn : _pillOff, GUILayout.Height(34 * u))) value = !value;
            GUILayout.Space(4 * u);
            return value;
        }

        // ---- non-host / running round / outside a level ----------------------
        private void DrawSimpleMenu(Rect body, float u)
        {
            GUILayout.BeginArea(body);
            if (!IsHost) GUILayout.Label(L.HostOnly, _mSmall);
            else if (!InLevel()) GUILayout.Label(L.OnlyInLevel, _mSmall);

            if (RoundActive)
            {
                GUILayout.Label($"<b>{L.RoundRunning}</b>", _mSmall);
                if (Seeker != null) GUILayout.Label(L.MonsterSpecialInfo($"{NameOf(Seeker)}: {_monsterName}", _monster != null ? Abilities.Name(_monster.Ability) : "-"), _mSmall);
                if (IsHost && GUILayout.Button(L.Abort, _chBtnMain, GUILayout.Height(44 * u))) Send(MsgEnd, (int)Winner.Aborted);
            }

            GUILayout.Space(12 * u);
            GUILayout.Label(L.ScoreTitle, _mSection);
            if (_sbNames.Length == 0) GUILayout.Label($"<color=#a89a9c>{L.NoScores}</color>", _mSmall);
            for (int i = 0; i < _sbNames.Length; i++)
                GUILayout.Label($"{i + 1}. {_sbNames[i]}  <b>{(i < _sbTotal.Length ? _sbTotal[i] : 0)}</b>", _mSmall);
            if (IsHost && _sbNames.Length > 0 && GUILayout.Button(L.ResetScores, _chBtn, GUILayout.Height(34 * u))) HostResetScores();
            GUILayout.EndArea();
        }
    }
}
