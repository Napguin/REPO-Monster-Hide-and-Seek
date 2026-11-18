using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Item phase at the start of a round: everyone stands still and picks their item (I). The hiding time only starts
    /// afterwards. It ends after PickSeconds (60 s) or as soon as everyone is done – taking your item counts as done,
    /// J votes to skip.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgPick = 31;
        private const int VotePickSkip = 3;

        // all PCs
        private float _pickEndAt;
        private int _pickVotes, _pickTotal;
        private bool _myPickSkip;

        // host
        private readonly Dictionary<int, bool> _hostPickSkip = new Dictionary<int, bool>();

        internal bool Picking => RoundActive && Phase == Phase.Hiding && Time.time < _pickEndAt;

        private bool RoundHasPicking => Plugin.PickSeconds.Value > 0f && (Plugin.FreeItem.Value || Plugin.Mode.Value == GameMode.Classic);

        private IEnumerator HostPickPhase(float seconds)
        {
            _hostPickSkip.Clear();
            // the start message comes back over the network a bit later: wait until the round really runs here
            for (float t = 0f; t < 5f && !(RoundActive && Phase == Phase.Hiding); t += Time.deltaTime) yield return null;
            if (!(RoundActive && Phase == Phase.Hiding)) { Plugin.Log.LogWarning("[Pick] round didn't start – no item time"); yield break; }
            Plugin.Log.LogInfo($"[Pick] item time {seconds:0} s");
            float end = Time.time + seconds;
            int last = -1;
            float sendAt = 0f;
            while (RoundActive && Phase == Phase.Hiding && Time.time < end)
            {
                int total = Mathf.Max(1, AllPlayers().Count());
                int votes = _hostPickSkip.Count(kv => kv.Value);
                if (votes >= total) { Plugin.Log.LogInfo($"[Pick] everyone is done ({votes}/{total})"); break; }
                if (votes != last || Time.time >= sendAt)
                {
                    Send(MsgPick, end - Time.time, votes, total);
                    last = votes;
                    sendAt = Time.time + 5f;
                }
                yield return null;
            }
            if (RoundActive) Send(MsgPick, 0f, 0, 0);
        }

        private void OnPick(float secondsLeft, int votes, int total)
        {
            if (!RoundActive) return;
            if (secondsLeft > 0f)
            {
                bool starting = _pickEndAt <= Time.time;
                _pickEndAt = Time.time + secondsLeft;
                _pickVotes = votes;
                _pickTotal = total;
                if (starting) { _myPickSkip = false; Banner(L.PickStart, new Color(0.4f, 0.7f, 1f), 4f); }
                return;
            }
            // item time over: now the hiding time starts
            _pickEndAt = 0f;
            _myPickSkip = false;
            if (Phase == Phase.Hiding) _phaseEndTime = Time.time + _hideDuration;
            Banner(L.PickOver, new Color(0.4f, 1f, 0.5f), 2.5f);
        }

        /// <summary>Per frame while picking: hiding time doesn't run, everyone stands still, J = skip, done = auto skip.</summary>
        private void PickTick()
        {
            if (!Picking) return;
            _phaseEndTime = Time.time + _hideDuration; // the hiding time hasn't started yet
            if (!CursorWanted) Compat.Call(PlayerController.instance, "InputDisable", 0.2f);

            bool done = LocalPickDone();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool key = kb != null && kb[Plugin.SkipKey.Value].wasPressedThisFrame && !GameRefs.IsTyping && !_menuOpen && !_debugOpen && !_freePickOpen;
            if (key) { _myPickSkip = !_myPickSkip; Send(MsgVote, VotePickSkip, _myPickSkip ? 1 : 0); }
            else if (done && !_myPickSkip) { _myPickSkip = true; Send(MsgVote, VotePickSkip, 1); }
        }

        /// <summary>The local player has nothing left to pick.</summary>
        private bool LocalPickDone()
        {
            var me = GameRefs.LocalAvatar;
            if (me == null) return true;
            bool seeker = Seeker != null && GameRefs.IsLocal(Seeker);
            if (seeker)
            {
                if (!_roundClassic) return true;                       // monster: nothing to pick
                return _startWeaponTaken && (!_roundFreeItem || _freeItemTaken);
            }
            return !_roundFreeItem || _freeItemTaken || !IsAlive(me);
        }

        private void HostOnPickSkip(int actor, bool skip) => _hostPickSkip[actor] = skip;

        private void DrawPick(float sw, float u)
        {
            if (!Picking) return;
            int left = Mathf.CeilToInt(_pickEndAt - Time.time);
            string text = L.PickCountdown(left) + "\n" + L.PickSkip(Plugin.SkipKey.Value.ToString(), _myPickSkip, _pickVotes, _pickTotal);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(20 * u), richText = true };
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(2 * u, 92 * u, sw, 80 * u), text, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 90 * u, sw, 80 * u), text, style);
        }
    }
}
