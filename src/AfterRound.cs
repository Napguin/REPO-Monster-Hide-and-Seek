using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// After a round (host-driven):
    ///  1. hiders that survived are shown to everyone for a few seconds,
    ///  2. every dead player is revived, healed, and everyone is teleported into the truck,
    ///  3. vote: who wants to be the next seeker, and stay on this map or change to another one.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgReveal = 14, MsgRegroup = 15, MsgVoteStart = 16, MsgVote = 17, MsgVoteState = 18, MsgVoteResult = 19;
        private const float RevealSeconds = 6f, VoteSeconds = 20f;

        // all clients
        private int[] _revealIds = new int[0];
        private float _revealEndAt;
        private bool _voteOpen;
        private float _voteEndAt;
        private string[] _voteMaps = new string[0];      // display names, [0] = stay
        private int[] _voteMapCounts = new int[0];
        private string[] _voteVolunteers = new string[0];
        private bool _myVolunteer;
        private int _myMapVote;
        private string _voteResultText;
        private float _voteResultUntil;

        // host
        private string[] _voteMapIds = new string[0];     // Level asset names, [0] = "" (stay)
        private readonly Dictionary<int, bool> _hostVolunteers = new Dictionary<int, bool>();
        private readonly Dictionary<int, int> _hostMapVotes = new Dictionary<int, int>();
        private Coroutine _afterRound;
        internal static string PendingLevel;              // used by the SetRunLevel patch
        private string _nextSeekerKey;

        // -----------------------------------------------------------------
        // Host flow
        // -----------------------------------------------------------------
        private void HostBeginAfterRound(List<int> survivors)
        {
            if (_afterRound != null) StopCoroutine(_afterRound);
            _afterRound = StartCoroutine(HostAfterRound(survivors));
        }

        private IEnumerator HostAfterRound(List<int> survivors)
        {
            yield return new WaitForSeconds(1.5f);
            if (survivors.Count > 0)
            {
                Send(MsgReveal, survivors.ToArray(), RevealSeconds);
                yield return new WaitForSeconds(RevealSeconds);
            }
            if (RoundActive) yield break;

            // revive the dead (game function, called by the host), then bring everyone to the truck
            int revived = 0;
            foreach (var p in AllPlayers())
                if (!IsAlive(p) && Compat.Call(p, "Revive", true)) revived++;
            Plugin.Log.LogInfo($"[AfterRound] revived {revived} player(s)");
            yield return new WaitForSeconds(revived > 0 ? 2f : 0.3f);
            Send(MsgRegroup, TruckPosition());
            yield return new WaitForSeconds(1.5f);
            if (RoundActive) yield break;

            // vote
            HostStartVote();
            float end = Time.time + VoteSeconds;
            while (Time.time < end && _voteOpen && !RoundActive) yield return null;
            if (!RoundActive) HostFinishVote();
            _afterRound = null;
        }

        private static Vector3 TruckPosition()
        {
            var sp = Compat.Static("TruckSafetySpawnPoint", "instance") as Component;
            if (sp != null) return sp.transform.position;
            try
            {
                var pts = SemiFunc.LevelPointsGetInStartRoom();
                if (pts != null && pts.Count > 0) return pts[0].transform.position;
            }
            catch { /* game changed */ }
            var local = GameRefs.LocalAvatar;
            return local != null ? local.transform.position : Vector3.zero;
        }

        private void HostStartVote()
        {
            _hostVolunteers.Clear();
            _hostMapVotes.Clear();
            var ids = new List<string> { "" };
            var names = new List<string> { L.VoteStay };
            try
            {
                var rm = RunManager.instance;
                var levels = rm != null ? rm.levels : null;
                if (levels != null)
                    foreach (var lv in levels.Where(l => l != null && l != rm.levelCurrent).OrderBy(_ => Random.value).Take(3))
                    {
                        ids.Add(lv.name);
                        names.Add(string.IsNullOrEmpty(lv.NarrativeName) ? lv.name : lv.NarrativeName);
                    }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[AfterRound] level list: " + ex.Message); }
            _voteMapIds = ids.ToArray();
            _voteOpen = true; // right away on the host – the network echo comes a bit later
            _voteEndAt = Time.time + VoteSeconds;
            Send(MsgVoteStart, names.ToArray(), VoteSeconds);
        }

        private int ActorOf(PlayerAvatar p) => p?.photonView?.Owner != null ? p.photonView.Owner.ActorNumber : -1;

        private PlayerAvatar AvatarOfActor(int actor)
        {
            if (actor < 0) return GameRefs.LocalAvatar;
            return AllPlayers().FirstOrDefault(p => ActorOf(p) == actor);
        }

        private void HostOnVote(int actor, int kind, int value)
        {
            if (!_voteOpen) return;
            if (kind == 0) _hostVolunteers[actor] = value != 0;
            else if (value >= 0 && value < _voteMapIds.Length) _hostMapVotes[actor] = value;
            HostSendVoteState();
        }

        private void HostSendVoteState()
        {
            var vol = _hostVolunteers.Where(kv => kv.Value).Select(kv => NameOf(AvatarOfActor(kv.Key))).ToArray();
            var counts = new int[_voteMapIds.Length];
            foreach (var v in _hostMapVotes.Values) if (v >= 0 && v < counts.Length) counts[v]++;
            Send(MsgVoteState, vol, counts, Mathf.Max(0f, _voteEndAt - Time.time));
        }

        private void HostFinishVote()
        {
            // seeker: random volunteer
            var volunteers = _hostVolunteers.Where(kv => kv.Value).Select(kv => AvatarOfActor(kv.Key)).Where(p => p != null).ToList();
            PlayerAvatar next = volunteers.Count > 0 ? volunteers[Random.Range(0, volunteers.Count)] : null;
            _selectedViewId = next != null ? next.photonView.ViewID : -1;
            _nextSeekerKey = next != null ? PlayerKey(next) : null;

            // map: most votes, ties keep the map
            var counts = new int[_voteMapIds.Length];
            foreach (var v in _hostMapVotes.Values) if (v >= 0 && v < counts.Length) counts[v]++;
            int win = 0;
            for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[win]) win = i;

            string seekerName = next != null ? NameOf(next) : "";
            Send(MsgVoteResult, seekerName, win);
            Plugin.Log.LogInfo($"[AfterRound] vote: seeker '{seekerName}', map option {win} ({(win > 0 ? _voteMapIds[win] : "stay")})");

            if (win > 0) StartCoroutine(HostChangeMap(_voteMapIds[win]));
        }

        /// <summary>Host: load a new map now (random or a given level asset), with the map size from the menu.</summary>
        internal void HostLoadMap(string levelId)
        {
            if (!IsHost || RoundActive) return;
            StartCoroutine(HostChangeMap(levelId, 0f));
        }

        private static int CurrentLevelNumber()
        {
            try { return RunManager.instance != null ? RunManager.instance.levelsCompleted + 1 : 1; } catch { return 1; }
        }

        private IEnumerator HostChangeMap(string levelId, float delay = 3f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (RoundActive) yield break;
            PendingLevel = levelId;
            var rm = RunManager.instance;
            // Map size: the level generator builds bigger maps for higher levels (levels completed).
            int want = Plugin.MapLevel.Value;
            if (rm != null && want > 0)
            {
                Compat.Set(rm, "levelsCompleted", want - 1);
                Compat.Call(rm, "UpdateMoonLevel");
                Plugin.Log.LogInfo($"[AfterRound] map size set to level {want}");
            }
            if (rm == null || !Compat.Call(rm, "ChangeLevel", false, false, RunManager.ChangeLevelType.RunLevel))
            {
                PendingLevel = null;
                Plugin.Log.LogWarning("[AfterRound] could not change the level");
            }
        }

        /// <summary>Host, after a map change: find the voted seeker again (view ids change with the level).</summary>
        private void ResolveNextSeeker()
        {
            if (string.IsNullOrEmpty(_nextSeekerKey)) return;
            var p = AllPlayers().FirstOrDefault(a => PlayerKey(a) == _nextSeekerKey);
            if (p != null) _selectedViewId = p.photonView.ViewID;
        }

        // -----------------------------------------------------------------
        // All clients
        // -----------------------------------------------------------------
        private void OnReveal(int[] ids, float seconds)
        {
            _revealIds = ids ?? new int[0];
            _revealEndAt = Time.time + seconds;
            Banner(L.RevealSurvivors(_revealIds.Length), new Color(0.4f, 1f, 0.5f), seconds);
        }

        private void OnRegroup(Vector3 truck)
        {
            _revealIds = new int[0];
            StartCoroutine(LocalRegroup(truck));
        }

        private IEnumerator LocalRegroup(Vector3 truck)
        {
            var me = GameRefs.LocalAvatar;
            if (me == null) yield break;
            if (!IsAlive(me)) Compat.Call(me, "Revive", true); // in case the host's revive didn't reach us
            // spread everyone a bit so nobody stands inside another player
            int idx = Mathf.Abs(ActorOf(me)) % 8;
            Vector3 pos = truck + Quaternion.Euler(0, idx * 45f, 0) * Vector3.forward * (idx == 0 ? 0f : 0.9f) + Vector3.up * 0.2f;
            for (int i = 0; i < 2; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 0.6f : 0.8f);
                TeleportLocal(pos);
            }
            var health = me.playerHealth;
            if (health != null)
            {
                int hp = Compat.Get(health, "health", 100), max = Compat.Get(health, "maxHealth", 100);
                if (hp < max) Compat.Call(health, "Heal", max - hp, true);
            }
            Banner(L.Regrouped, Color.white, 3f);
        }

        private static void TeleportLocal(Vector3 pos)
        {
            var pc = PlayerController.instance;
            if (pc == null) return;
            var rb = Compat.Get<Rigidbody>(pc, "rb");
            pc.transform.position = pos;
            if (rb != null) { rb.position = pos; rb.velocity = Vector3.zero; }
            var me = GameRefs.LocalAvatar;
            if (me != null) me.transform.position = pos;
        }

        private void OnVoteStart(string[] maps, float seconds)
        {
            _voteOpen = true;
            _voteEndAt = Time.time + seconds;
            _voteMaps = maps ?? new string[0];
            _voteMapCounts = new int[_voteMaps.Length];
            _voteVolunteers = new string[0];
            _myVolunteer = false;
            _myMapVote = 0;
            Send(MsgVote, 1, 0); // default: stay
        }

        private void OnVoteState(string[] volunteers, int[] counts, float left)
        {
            _voteVolunteers = volunteers ?? new string[0];
            _voteMapCounts = counts ?? new int[0];
            _voteEndAt = Time.time + left;
        }

        private void OnVoteResult(string seeker, int map)
        {
            _voteOpen = false;
            string m = map > 0 && map < _voteMaps.Length ? _voteMaps[map] : null;
            _voteResultText = L.VoteResult(seeker, m);
            _voteResultUntil = Time.time + 8f;
            Banner(_voteResultText, new Color(1f, 0.85f, 0.4f), 6f);
        }

        // -----------------------------------------------------------------
        // GUI
        // -----------------------------------------------------------------
        private void DrawRevealSurvivors(float u)
        {
            if (Time.time >= _revealEndAt || _revealIds.Length == 0) return;
            var cam = GameRefs.MainCamera;
            if (cam == null) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(18 * u) };
            Vector3 saved = cam.transform.position;
            if (ThirdPersonCamera.Working) cam.transform.position += ThirdPersonCamera.LastOffset;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
            foreach (int id in _revealIds)
            {
                var p = FindByViewId(id);
                if (p == null) continue;
                Vector3 wp = PlayerPos(p) + Vector3.up * 2f;
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0) continue;
                float x = sp.x * Screen.width / cam.pixelWidth, y = Screen.height - sp.y * Screen.height / cam.pixelHeight;
                float s = 20 * u;
                GUI.color = new Color(0.4f, 1f, 0.5f, pulse);
                GUI.DrawTexture(new Rect(x - s / 2, y - s / 2, s, s), _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(x - 140 * u, y + s * 0.6f, 280 * u, 40 * u), $"{NameOf(p)}  {Vector3.Distance(saved, wp):0}m", style);
            }
            cam.transform.position = saved;
        }

        private bool VoteVisible => _voteOpen && !RoundActive;

        private void DrawVote(float sw, float sh, float u)
        {
            if (!VoteVisible) return;
            EnsureMenuStyles(u);
            float w = Mathf.Min(sw - 40 * u, 620 * u), h = Mathf.Min(sh - 40 * u, (250 + 52 * _voteMaps.Length) * u);
            var r = new Rect((sw - w) / 2f, sh * 0.12f, w, h);
            GUI.DrawTexture(r, Tex(new Color(0.06f, 0.045f, 0.05f, 0.96f)));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4 * u), Tex(new Color(0.8f, 0.1f, 0.08f, 1f)));
            float pad = 20 * u, y = r.y + 14 * u;
            int left = Mathf.Max(0, Mathf.CeilToInt(_voteEndAt - Time.time));
            GUI.Label(new Rect(r.x + pad, y, w - 2 * pad, 36 * u), $"{L.VoteTitle}  <color=#a89a9c>{left}s</color>", _mSection);
            y += 44 * u;

            // seeker volunteer
            string vol = _myVolunteer ? $"[{L.On}]  {L.VoteWantSeeker}" : $"[{L.Off}]  {L.VoteWantSeeker}";
            if (GUI.Button(new Rect(r.x + pad, y, w - 2 * pad, 42 * u), vol, _myVolunteer ? _cardSelStyle : _cardStyle))
            {
                _myVolunteer = !_myVolunteer;
                Send(MsgVote, 0, _myVolunteer ? 1 : 0);
            }
            y += 48 * u;
            string list = _voteVolunteers.Length > 0 ? string.Join(", ", _voteVolunteers) : L.VoteNoVolunteers;
            GUI.Label(new Rect(r.x + pad, y, w - 2 * pad, 26 * u), $"<color=#a89a9c>{list}</color>", _mSmall);
            y += 36 * u;

            // map
            GUI.Label(new Rect(r.x + pad, y, w - 2 * pad, 30 * u), L.VoteMap, _mSection);
            y += 36 * u;
            for (int i = 0; i < _voteMaps.Length; i++)
            {
                int c = i < _voteMapCounts.Length ? _voteMapCounts[i] : 0;
                if (GUI.Button(new Rect(r.x + pad, y, w - 2 * pad, 44 * u), $"<b>{_voteMaps[i]}</b>   <color=#a89a9c>{c}</color>", _myMapVote == i ? _cardSelStyle : _cardStyle))
                {
                    _myMapVote = i;
                    Send(MsgVote, 1, i);
                }
                y += 50 * u;
            }

            if (IsHost && GUI.Button(new Rect(r.x + pad, r.yMax - 52 * u, w - 2 * pad, 40 * u), L.VoteFinish, _chBtnMain))
                _voteOpen = false; // host coroutine sees this and finishes the vote
        }
    }

    /// <summary>Map vote: make the game load the voted level instead of a random one.</summary>
    [HarmonyPatch(typeof(RunManager), "SetRunLevel")]
    internal static class VotedLevelPatch
    {
        private static void Postfix(RunManager __instance)
        {
            var id = HideAndSeekManager.PendingLevel;
            if (string.IsNullOrEmpty(id) || __instance == null) return;
            HideAndSeekManager.PendingLevel = null;
            var lv = __instance.levels?.FirstOrDefault(l => l != null && l.name == id);
            if (lv == null) return;
            __instance.levelCurrent = lv;
            Plugin.Log.LogInfo($"[AfterRound] voted level set: {lv.name}");
        }
    }
}
