using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Between two rounds (How To Play): time to look at the hiding spots (skip vote), the monster keeps its form,
    /// then truck + vote, and the next round starts by itself. At every round start all items with a battery
    /// (weapons, melee durability, equipment) are recharged.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgInspect = 30;
        private const int VoteSkip = 2;

        // all PCs
        private float _inspectEndAt;
        private int _inspectSkip, _inspectTotal;
        private bool _mySkip;
        private MonsterVisual _afterMonster; // the monster of the finished round, kept until the next one

        // host
        private readonly Dictionary<int, bool> _hostSkip = new Dictionary<int, bool>();
        private float _nextRoundAt;
        private Object _levelAtMapChange; // the next round waits for a different (new) level

        private bool Inspecting => !RoundActive && Time.time < _inspectEndAt;

        private void OnInspect(float secondsLeft, int skip, int total)
        {
            bool starting = !Inspecting && secondsLeft > 0f;
            _inspectEndAt = secondsLeft > 0f ? Time.time + secondsLeft : 0f;
            _inspectSkip = skip;
            _inspectTotal = total;
            if (secondsLeft <= 0f) { _mySkip = false; _revealIds = new int[0]; return; }
            if (!starting) return;
            _mySkip = false;
            // everyone alive and healthy again, right where they are
            var me = GameRefs.LocalAvatar;
            if (me == null) return;
            if (!IsAlive(me)) Compat.Call(me, "Revive", true);
            // a revive starts at 1 HP and needs a moment over the network: heal a few times
            Later(1.5f, HealLocalFull);
            Later(3.5f, HealLocalFull);
            Later(6f, HealLocalFull);
            Banner(L.InspectStart, new Color(0.5f, 0.85f, 1f), 4f);
        }

        private void HealLocalFull()
        {
            var me = GameRefs.LocalAvatar;
            var h = me != null ? me.playerHealth : null;
            if (h == null || !IsAlive(me)) return;
            int hp = Compat.Get(h, "health", 100), max = Compat.Get(h, "maxHealth", 100);
            if (hp < max) Compat.Call(h, "Heal", max - hp, true);
        }

        private void InspectKeys()
        {
            if (!Inspecting || GameRefs.IsTyping || _menuOpen || _debugOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb[Plugin.SkipKey.Value].wasPressedThisFrame) return;
            _mySkip = !_mySkip;
            Send(MsgVote, VoteSkip, _mySkip ? 1 : 0);
        }

        private void HostOnSkip(int actor, bool skip) => _hostSkip[actor] = skip;

        private void KeepMonsterAfterRound()
        {
            if (_afterMonster != null) _afterMonster.Remove();
            _afterMonster = _monster;
            if (_afterMonster != null && _afterMonster.IsLocalMonster) _afterMonster.SetBodyVisible(false); // back in first person
        }

        private void RemoveAfterMonster()
        {
            if (_afterMonster != null) _afterMonster.Remove();
            _afterMonster = null;
        }

        private void HostScheduleNextRound(bool mapChange)
        {
            _levelAtMapChange = mapChange ? LevelGenerator.Instance : null;
            _nextRoundAt = Time.time + (mapChange ? 6f : 3f);
        }

        /// <summary>Lobby, shop, main menu, arena ... – anything that isn't a playable level (a map change in between
        /// two levels is not counted, so the planned next round survives it).</summary>
        private static bool OutsideLevels()
        {
            try { return SemiFunc.RunIsLobbyMenu() || SemiFunc.RunIsLobby() || SemiFunc.RunIsShop() || SemiFunc.MenuLevel() || SemiFunc.IsMainMenu() || SemiFunc.RunIsArena(); }
            catch { return false; }
        }

        /// <summary>Left the level (lobby restarted, back to the menu ...): forget everything of the after-round.</summary>
        private void CancelAfterRound()
        {
            if (_afterRound != null) { StopCoroutine(_afterRound); _afterRound = null; }
            bool any = _voteOpen || _inspectEndAt > 0f || _nextRoundAt > 0f || _afterMonster != null || _revealIds.Length > 0;
            _voteOpen = false;
            _inspectEndAt = 0f;
            _nextRoundAt = 0f;
            _revealIds = new int[0];
            _voteResultUntil = 0f;
            RemoveAfterMonster();
            if (any) Plugin.Log.LogInfo("[AfterRound] cancelled – not in a level anymore");
        }

        private void NextRoundTick()
        {
            if (OutsideLevels()) { CancelAfterRound(); return; }
            if (RoundActive) { _nextRoundAt = 0f; return; }
            if (_nextRoundAt <= 0f || !IsHost || Time.time < _nextRoundAt || _voteOpen) return;
            if (!InLevel() || !LevelReady()) return; // a voted map is still loading
            ResolveNextSeeker();
            HostStartRound();
            if (RoundActive) { _nextRoundAt = 0f; Plugin.Log.LogInfo("[AfterRound] next round started automatically"); }
            else _nextRoundAt = Time.time + 5f; // not enough players yet – try again
        }

        private float _levelReadySince;

        /// <summary>The level is generated, the game is running (not loading) and that has been so for a few seconds
        /// (everyone has spawned in the truck of the new map).</summary>
        private bool LevelReady()
        {
            var lg = LevelGenerator.Instance;
            var gd = GameDirector.instance;
            bool ready = lg != null && Compat.Get(lg, "Generated", true)
                         && (_levelAtMapChange == null || lg != _levelAtMapChange || Time.time > _nextRoundAt + 35f) // not the old map anymore (fallback after 35 s)
                         && gd != null && gd.currentState == GameDirector.gameState.Main;
            if (!ready) { _levelReadySince = 0f; return false; }
            if (_levelReadySince <= 0f) _levelReadySince = Time.time;
            return Time.time - _levelReadySince >= (_levelAtMapChange != null ? 6f : 0f);
        }

        private void DrawInspect(float sw, float u)
        {
            if (!Inspecting) return;
            int left = Mathf.CeilToInt(_inspectEndAt - Time.time);
            string text = L.InspectCountdown(left) + "\n" + L.InspectSkip(Plugin.SkipKey.Value.ToString(), _mySkip, _inspectSkip, _inspectTotal);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(20 * u), richText = true };
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(2 * u, 62 * u, sw, 80 * u), text, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 60 * u, sw, 80 * u), text, style);
        }

        /// <summary>Host, at round start: every battery item (guns, melee durability, drones ...) full again.</summary>
        private void HostRechargeAll()
        {
            int n = 0;
            foreach (var b in Object.FindObjectsOfType<ItemBattery>())
            {
                if (b == null) continue;
                if (Compat.Call(b, "SetBatteryLife", 100)) n++;
                else { Compat.Set(b, "batteryLife", 100f); n++; }
            }
            Plugin.Log.LogInfo($"[Round] recharged {n} item(s)");
        }
    }
}
