using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HideAndSeekMonster
{
    internal enum Phase
    {
        None,
        Hiding,
        Hunting
    }

    internal enum Winner
    {
        Seeker = 0,
        Hiders = 1,
        Aborted = 2
    }

    /// <summary>
    /// Host-authoritative round logic + networking (Photon RaiseEvent) + IMGUI.
    /// Every player needs the mod installed.
    /// </summary>
    public class HideAndSeekManager : MonoBehaviour, IOnEventCallback
    {
        public static HideAndSeekManager Instance { get; private set; }

        private const byte EventCode = 147;
        private const string Magic = "HNSM";
        private const int MsgStart = 1, MsgKill = 2, MsgEnd = 3;

        // ---- round state (all clients) ----
        internal Phase Phase { get; private set; } = Phase.None;
        internal bool RoundActive => Phase != Phase.None;
        internal PlayerAvatar Seeker { get; private set; }
        internal bool SeekerInvincibleThisRound { get; private set; }

        private int _seekerViewId = -1;
        private float _phaseEndTime;
        private float _roundHuntTime;
        private float _roundSpeed = 1f;
        private bool _roundInfStamina;
        private bool _roundBlind;
        private float _roundScale = 1f;
        private MonsterVisual _monster;
        private readonly HashSet<int> _hiderViewIds = new HashSet<int>();

        // ---- host only ----
        private readonly Dictionary<int, float> _killCooldown = new Dictionary<int, float>();
        private float _enemyTick;

        // ---- UI ----
        private bool _menuOpen;
        private Rect _window = new Rect(40, 40, 380, 520);
        private Vector2 _scroll;
        private int _selectedViewId = -1; // -1 = random
        private string _bannerText;
        private Color _bannerColor = Color.white;
        private float _bannerUntil;
        private Texture2D _white;
        private GUIStyle _bigStyle, _hudStyle;

        private static bool IsHost => !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient;

        private void Awake()
        {
            Instance = this;
            _white = Texture2D.whiteTexture;
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        // =====================================================================
        // Update
        // =====================================================================
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[Plugin.MenuKey.Value].wasPressedThisFrame)
                _menuOpen = !_menuOpen;

            if (_menuOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (CursorManager.instance != null) CursorManager.instance.Unlock(0.2f);
                if (PlayerController.instance != null) PlayerController.instance.InputDisable(0.2f);
            }

            if (!RoundActive) return;

            // Seeker gone (left the game / level changed) -> stop the round locally.
            if (Seeker == null || !InLevel())
            {
                EndRoundLocal(Winner.Aborted, false);
                return;
            }

            if (Phase == Phase.Hiding && Time.time >= _phaseEndTime)
            {
                Phase = Phase.Hunting;
                _phaseEndTime = Time.time + _roundHuntTime;
                Banner(L.HuntStarted, new Color(1f, 0.2f, 0.2f), 3f);
            }

            ApplyLocalSeekerEffects();

            if (IsHost) HostTick();
        }

        private void ApplyLocalSeekerEffects()
        {
            if (Seeker == null || !Seeker.isLocal) return;
            var pc = PlayerController.instance;
            if (pc == null) return;

            if (Phase == Phase.Hiding)
            {
                pc.InputDisable(0.2f); // frozen while the others hide
            }
            else if (Phase == Phase.Hunting)
            {
                if (!Mathf.Approximately(_roundSpeed, 1f)) pc.OverrideSpeed(_roundSpeed, 0.2f);
                if (_roundInfStamina) pc.EnergyCurrent = pc.EnergyStart;
            }
        }

        private void HostTick()
        {
            // Keep normal enemies away during the round.
            if (Plugin.DisableEnemies.Value)
            {
                _enemyTick -= Time.deltaTime;
                if (_enemyTick <= 0f)
                {
                    _enemyTick = 1f;
                    DespawnEnemies();
                }
            }

            var hiders = AliveHiders();

            if (Phase == Phase.Hunting)
            {
                Vector3 sp = Seeker.transform.position;
                float range = Plugin.KillRange.Value;
                foreach (var h in hiders)
                {
                    int id = h.photonView.ViewID;
                    if (_killCooldown.TryGetValue(id, out float until) && Time.time < until) continue;
                    if (Vector3.Distance(sp, h.transform.position) <= range)
                    {
                        _killCooldown[id] = Time.time + 2f;
                        Send(MsgKill, id);
                    }
                }

                if (hiders.Count == 0)
                {
                    Send(MsgEnd, (int)Winner.Seeker);
                    return;
                }

                if (Time.time >= _phaseEndTime)
                {
                    Send(MsgEnd, (int)Winner.Hiders);
                }
            }
            else if (Phase == Phase.Hiding && hiders.Count == 0)
            {
                Send(MsgEnd, (int)Winner.Aborted);
            }
        }

        private static void DespawnEnemies()
        {
            var dir = EnemyDirector.instance;
            if (dir == null || dir.enemiesSpawned == null) return;
            foreach (var e in dir.enemiesSpawned)
            {
                if (e == null) continue;
                try
                {
                    if (e.Spawned) e.Despawn();
                    if (e.DespawnedTimer < 30f) e.DespawnedTimer = 30f;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogDebug("Despawn failed: " + ex.Message);
                }
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        private static bool InLevel()
        {
            try { return SemiFunc.RunIsLevel(); }
            catch { return false; }
        }

        private static List<PlayerAvatar> AllPlayers()
        {
            var list = SemiFunc.PlayerGetAll();
            return list == null ? new List<PlayerAvatar>() : list.Where(p => p != null).ToList();
        }

        private static bool IsAlive(PlayerAvatar p) => p != null && !p.isDisabled && !p.deadSet;

        private List<PlayerAvatar> AliveHiders() =>
            AllPlayers().Where(p => _hiderViewIds.Contains(p.photonView.ViewID) && IsAlive(p)).ToList();

        private static PlayerAvatar FindByViewId(int id) =>
            AllPlayers().FirstOrDefault(p => p.photonView != null && p.photonView.ViewID == id);

        private static string NameOf(PlayerAvatar p)
        {
            if (p == null) return "?";
            try
            {
                var n = SemiFunc.PlayerGetName(p);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { /* ignore */ }
            return string.IsNullOrEmpty(p.playerName) ? "Player" : p.playerName;
        }

        private void Banner(string text, Color color, float seconds)
        {
            _bannerText = text;
            _bannerColor = color;
            _bannerUntil = Time.time + seconds;
        }

        // =====================================================================
        // Networking
        // =====================================================================
        private void Send(int type, params object[] args)
        {
            var payload = new object[args.Length + 2];
            payload[0] = Magic;
            payload[1] = type;
            args.CopyTo(payload, 2);

            if (SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom)
            {
                PhotonNetwork.RaiseEvent(EventCode, payload,
                    new RaiseEventOptions { Receivers = ReceiverGroup.All },
                    SendOptions.SendReliable);
            }
            else
            {
                Handle(payload, true);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCode) return;
            if (!(photonEvent.CustomData is object[] data)) return;

            // Only accept round messages from the master client.
            bool fromMaster = true;
            if (PhotonNetwork.CurrentRoom != null)
            {
                var sender = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
                fromMaster = sender != null && sender.IsMasterClient;
            }
            Handle(data, fromMaster);
        }

        private void Handle(object[] data, bool fromMaster)
        {
            if (data.Length < 2 || !(data[0] is string m) || m != Magic || !fromMaster) return;
            try
            {
                switch ((int)data[1])
                {
                    case MsgStart:
                        OnStart((int)data[2], (float)data[3], (float)data[4], (float)data[5],
                            (bool)data[6], (bool)data[7], (bool)data[8], (float)data[9], (int[])data[10]);
                        break;
                    case MsgKill:
                        OnKill((int)data[2]);
                        break;
                    case MsgEnd:
                        EndRoundLocal((Winner)(int)data[2], true);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("Bad Hide&Seek message: " + ex);
            }
        }

        // =====================================================================
        // Round flow
        // =====================================================================
        private void HostStartRound()
        {
            if (!IsHost || RoundActive || !InLevel()) return;
            var alive = AllPlayers().Where(IsAlive).ToList();
            if (alive.Count < 2 && SemiFunc.IsMultiplayer()) return;
            if (alive.Count == 0) return;

            PlayerAvatar seeker = _selectedViewId >= 0 ? alive.FirstOrDefault(p => p.photonView.ViewID == _selectedViewId) : null;
            if (seeker == null) seeker = alive[Random.Range(0, alive.Count)];

            int[] hiders = alive.Where(p => p != seeker).Select(p => p.photonView.ViewID).ToArray();

            _killCooldown.Clear();
            Send(MsgStart,
                seeker.photonView.ViewID,
                Plugin.HideTime.Value,
                Plugin.RoundTime.Value,
                Plugin.SeekerSpeed.Value,
                Plugin.SeekerInfiniteStamina.Value,
                Plugin.SeekerInvincible.Value,
                Plugin.BlindSeekerWhileHiding.Value,
                Plugin.MonsterScale.Value,
                hiders);
            _menuOpen = false;
        }

        private void OnStart(int seekerId, float hide, float hunt, float speed, bool infStamina,
            bool invincible, bool blind, float scale, int[] hiders)
        {
            if (RoundActive) EndRoundLocal(Winner.Aborted, false);

            var seeker = FindByViewId(seekerId);
            if (seeker == null)
            {
                Plugin.Log.LogWarning("Seeker not found for view id " + seekerId);
                return;
            }

            Seeker = seeker;
            _seekerViewId = seekerId;
            _roundHuntTime = hunt;
            _roundSpeed = speed;
            _roundInfStamina = infStamina;
            SeekerInvincibleThisRound = invincible;
            _roundBlind = blind;
            _roundScale = scale;
            _hiderViewIds.Clear();
            foreach (var h in hiders) _hiderViewIds.Add(h);

            Phase = Phase.Hiding;
            _phaseEndTime = Time.time + hide;

            // Everyone except the seeker himself sees the monster model
            // (on the seeker's own client it would block the first-person camera).
            if (!seeker.isLocal)
                _monster = MonsterVisual.Attach(seeker, _roundScale);

            Plugin.Log.LogInfo($"Round started. Seeker: {NameOf(seeker)}, hiders: {hiders.Length}");
        }

        private void OnKill(int victimId)
        {
            var victim = FindByViewId(victimId);
            if (victim == null) return;
            Banner(L.Caught(NameOf(victim)), new Color(1f, 0.35f, 0.2f), 3f);

            if (!victim.isLocal || !IsAlive(victim)) return;

            // Kill the local player.
            var health = victim.playerHealth;
            try
            {
                if (health != null)
                {
                    health.godMode = false;
                    health.invincibleTimer = 0f;
                    health.Hurt(Mathf.Max(health.health, health.maxHealth) + 999, false);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Hurt failed: " + ex.Message);
            }
            StartCoroutine(EnsureDead(victim));
        }

        private System.Collections.IEnumerator EnsureDead(PlayerAvatar victim)
        {
            yield return new WaitForSeconds(0.4f);
            if (victim != null && IsAlive(victim))
            {
                try { victim.PlayerDeath(-1); }
                catch (System.Exception ex) { Plugin.Log.LogError("PlayerDeath failed: " + ex); }
            }
        }

        private void EndRoundLocal(Winner winner, bool showBanner)
        {
            if (!RoundActive) return;
            string seekerName = NameOf(Seeker);

            if (_monster != null) _monster.Remove();
            _monster = null;

            Phase = Phase.None;
            Seeker = null;
            _seekerViewId = -1;
            _hiderViewIds.Clear();
            _killCooldown.Clear();
            SeekerInvincibleThisRound = false;

            if (!showBanner) return;
            switch (winner)
            {
                case Winner.Seeker: Banner(L.SeekerWins(seekerName), new Color(1f, 0.2f, 0.2f), 8f); break;
                case Winner.Hiders: Banner(L.HidersWin, new Color(0.3f, 1f, 0.4f), 8f); break;
                default: Banner(L.Aborted, Color.white, 4f); break;
            }
        }

        // =====================================================================
        // GUI
        // =====================================================================
        private void EnsureStyles()
        {
            float s = Screen.height / 1080f;
            if (_bigStyle == null)
            {
                _bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                _hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            }
            _bigStyle.fontSize = Mathf.RoundToInt(44 * s);
            _hudStyle.fontSize = Mathf.RoundToInt(24 * s);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float sw = Screen.width, sh = Screen.height;
            int secs = Mathf.Max(0, Mathf.CeilToInt(_phaseEndTime - Time.time));
            bool localIsSeeker = RoundActive && Seeker != null && Seeker.isLocal;

            if (RoundActive)
            {
                if (localIsSeeker && Phase == Phase.Hiding)
                {
                    if (_roundBlind)
                    {
                        GUI.color = Color.black;
                        GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
                    }
                    GUI.color = new Color(1f, 0.15f, 0.15f);
                    ShadowLabel(new Rect(0, sh * 0.38f, sw, 80 * sh / 1080f), L.YouAreSeeker, _bigStyle);
                    GUI.color = Color.white;
                    ShadowLabel(new Rect(0, sh * 0.48f, sw, 80 * sh / 1080f), L.WaitForHiders(secs), _bigStyle);
                }
                else if (localIsSeeker && Phase == Phase.Hunting)
                {
                    // red tint for the monster
                    GUI.color = new Color(0.6f, 0f, 0f, 0.18f);
                    GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
                }
                else if (Phase == Phase.Hiding)
                {
                    GUI.color = new Color(1f, 0.85f, 0.2f);
                    ShadowLabel(new Rect(0, sh * 0.15f, sw, 80 * sh / 1080f), L.HideNow(NameOf(Seeker), secs), _bigStyle);
                }

                GUI.color = Color.white;
                string hud = Phase == Phase.Hiding ? L.Hiding(secs) : L.Hunt(secs, CountAliveHidersLocal());
                ShadowLabel(new Rect(0, 12, sw, 40), hud, _hudStyle);
            }

            if (!string.IsNullOrEmpty(_bannerText) && Time.time < _bannerUntil)
            {
                GUI.color = _bannerColor;
                ShadowLabel(new Rect(0, sh * 0.62f, sw, 90 * sh / 1080f), _bannerText, _bigStyle);
            }
            GUI.color = Color.white;

            if (_menuOpen)
                _window = GUILayout.Window(0x48A5, _window, DrawWindow, L.MenuTitle);
        }

        private int CountAliveHidersLocal() =>
            AllPlayers().Count(p => _hiderViewIds.Contains(p.photonView.ViewID) && IsAlive(p));

        private static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var c = GUI.color;
            GUI.color = new Color(0, 0, 0, c.a * 0.85f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            GUI.color = c;
            GUI.Label(r, text, style);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4);

            if (!IsHost)
            {
                GUILayout.Label(L.HostOnly);
                if (RoundActive) GUILayout.Label(L.RoundRunning);
                if (GUILayout.Button(L.Close)) _menuOpen = false;
                GUI.DragWindow();
                return;
            }

            if (RoundActive)
            {
                GUILayout.Label(L.RoundRunning);
                if (GUILayout.Button(L.Abort, GUILayout.Height(32))) Send(MsgEnd, (int)Winner.Aborted);
                if (GUILayout.Button(L.Close)) _menuOpen = false;
                GUI.DragWindow();
                return;
            }

            if (!InLevel())
            {
                GUILayout.Label(L.OnlyInLevel);
                if (GUILayout.Button(L.Close)) _menuOpen = false;
                GUI.DragWindow();
                return;
            }

            GUILayout.Label(L.ChooseSeeker);
            var alive = AllPlayers().Where(IsAlive).ToList();
            if (_selectedViewId >= 0 && !alive.Any(p => p.photonView.ViewID == _selectedViewId)) _selectedViewId = -1;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(180));
            if (GUILayout.Toggle(_selectedViewId < 0, "  " + L.Random)) _selectedViewId = -1;
            foreach (var p in alive)
            {
                int vid = p.photonView.ViewID;
                bool sel = _selectedViewId == vid;
                if (GUILayout.Toggle(sel, "  " + NameOf(p)) && !sel) _selectedViewId = vid;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            Plugin.HideTime.Value = Slider(L.HideTime, Plugin.HideTime.Value, 5f, 120f, "s", 0);
            Plugin.RoundTime.Value = Slider(L.RoundTime, Plugin.RoundTime.Value, 30f, 900f, "s", 0);
            Plugin.KillRange.Value = Slider(L.KillRange, Plugin.KillRange.Value, 0.5f, 4f, "m", 1);
            Plugin.SeekerSpeed.Value = Slider(L.SeekerSpeed, Plugin.SeekerSpeed.Value, 0.5f, 2.5f, "x", 2);
            Plugin.DisableEnemies.Value = GUILayout.Toggle(Plugin.DisableEnemies.Value, " " + L.DisableEnemies);
            Plugin.SeekerInvincible.Value = GUILayout.Toggle(Plugin.SeekerInvincible.Value, " " + L.Invincible);
            Plugin.BlindSeekerWhileHiding.Value = GUILayout.Toggle(Plugin.BlindSeekerWhileHiding.Value, " " + L.Blind);

            GUILayout.Space(8);
            bool enough = alive.Count >= 2 || !SemiFunc.IsMultiplayer();
            if (!enough) GUILayout.Label(L.NotEnoughPlayers);
            GUI.enabled = enough;
            if (GUILayout.Button(L.Start, GUILayout.Height(36))) HostStartRound();
            GUI.enabled = true;
            if (GUILayout.Button(L.Close)) _menuOpen = false;
            GUI.DragWindow();
        }

        private static float Slider(string label, float value, float min, float max, string unit, int decimals)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value.ToString("F" + decimals)}{unit}", GUILayout.Width(170));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            if (decimals == 0) value = Mathf.Round(value);
            return value;
        }
    }
}
