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
        private const int MsgStart = 1, MsgKill = 2, MsgEnd = 3, MsgAttack = 4, MsgRoar = 5;
        private const string RandomMonster = "<random>";

        // ---- round state (all clients) ----
        internal Phase Phase { get; private set; } = Phase.None;
        internal bool RoundActive => Phase != Phase.None;
        internal PlayerAvatar Seeker { get; private set; }
        internal bool SeekerInvincibleThisRound { get; private set; }

        private float _phaseEndTime;
        private float _roundHuntTime;
        private float _roundSpeed = 1f;
        private bool _roundInfStamina;
        private bool _roundBlind;
        private float _roundAttackRange = 2.8f;
        private MonsterVisual _monster;
        private string _monsterName;
        private readonly HashSet<int> _hiderViewIds = new HashSet<int>();

        // ---- local seeker ----
        private bool _thirdPerson = true;
        private float _attackReadyAt, _roarReadyAt, _lungeUntil;
        private InputAction _attackAction, _roarAction, _viewAction;

        // ---- test mode (host-local) ----
        private bool _roundTest;
        private readonly List<TestDummy> _dummies = new List<TestDummy>();
        private int _dummyCounter;
        private bool _debugOverlay = true;
        private string _lastAttackInfo = "-";
        private int _attackCount, _killCount;
        private GUIStyle _debugStyle;

        // ---- host only ----
        private readonly Dictionary<int, float> _killCooldown = new Dictionary<int, float>();
        private float _enemyTick;

        // ---- UI ----
        private bool _menuOpen;
        private Rect _window = new Rect(40, 40, 400, 640);
        private Vector2 _scroll, _scrollMonsters;
        private int _selectedViewId = -1; // -1 = random
        private string _selectedMonster = RandomMonster;
        private string _bannerText;
        private Color _bannerColor = Color.white;
        private float _bannerUntil;
        private float _flashUntil;
        private Texture2D _white;
        private GUIStyle _bigStyle, _hudStyle, _smallStyle;

        private static bool IsHost => !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient;

        private void Awake()
        {
            Instance = this;
            _white = Texture2D.whiteTexture;
            ThirdPersonCamera.Hook();
            _attackAction = MakeAction(Plugin.AttackBinding.Value, "<Mouse>/leftButton");
            _roarAction = MakeAction(Plugin.RoarBinding.Value, "<Mouse>/rightButton");
            _viewAction = MakeAction(Plugin.ToggleViewBinding.Value, "<Keyboard>/v");
        }

        private static InputAction MakeAction(string binding, string fallback)
        {
            InputAction a;
            try { a = new InputAction(binding: string.IsNullOrWhiteSpace(binding) ? fallback : binding); }
            catch { a = new InputAction(binding: fallback); }
            a.Enable();
            return a;
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

            if (!RoundActive)
            {
                ThirdPersonCamera.Active = false;
                return;
            }

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
                if (_monster != null) _monster.PlayRoar();
            }

            LocalSeekerTick();

            if (IsHost) HostTick();
        }

        private void LocalSeekerTick()
        {
            bool localSeeker = Seeker != null && Seeker.isLocal;
            ThirdPersonCamera.Active = localSeeker && Phase == Phase.Hunting && _thirdPerson;
            ThirdPersonCamera.Distance = Plugin.CameraDistance.Value;
            if (!localSeeker) return;

            // In first person the own monster body would block the view.
            if (_monster != null) _monster.SetBodyVisible(ThirdPersonCamera.Active);

            var pc = PlayerController.instance;
            if (pc == null) return;

            // A monster has no hands for grabbing.
            if (PhysGrabber.instance != null) PhysGrabber.instance.grabDisableTimer = 0.25f;

            if (Phase == Phase.Hiding)
            {
                pc.InputDisable(0.2f); // frozen while the others hide
                return;
            }

            float speed = _roundSpeed * (Time.time < _lungeUntil ? 1.8f : 1f);
            if (!Mathf.Approximately(speed, 1f)) pc.OverrideSpeed(speed, 0.2f);
            if (_roundInfStamina) pc.EnergyCurrent = pc.EnergyStart;

            if (_menuOpen || IsChatOpen()) return;

            if (_viewAction.WasPressedThisFrame()) _thirdPerson = !_thirdPerson;
            if (_roundTest) TestKeys();

            if (_attackAction.WasPressedThisFrame() && Time.time >= _attackReadyAt)
            {
                _attackReadyAt = Time.time + Plugin.AttackCooldown.Value;
                _lungeUntil = Time.time + 0.35f;
                var cam = Camera.main != null ? Camera.main.transform : Seeker.transform;
                Send(MsgAttack, Seeker.transform.position, cam.forward);
            }

            if (_roarAction.WasPressedThisFrame() && Time.time >= _roarReadyAt)
            {
                _roarReadyAt = Time.time + Plugin.RoarCooldown.Value;
                Send(MsgRoar, Seeker.transform.position);
            }
        }

        private static bool IsChatOpen() => ChatManager.instance != null && ChatManager.instance.chatActive;

        private void HostTick()
        {
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
                // Touching a hider also kills.
                Vector3 sp = Seeker.transform.position;
                foreach (var h in hiders)
                    if (Vector3.Distance(sp, h.transform.position) <= Plugin.KillRange.Value)
                        HostKill(h);
                foreach (var d in AliveDummies())
                    if (FlatDistance(sp, d.transform.position) <= Plugin.KillRange.Value)
                        KillDummy(d, sp, "touch");

                // Test mode keeps running so you can respawn dummies (F10) and try more monsters.
                if (hiders.Count == 0 && AliveDummies().Count == 0 && !_roundTest)
                {
                    Send(MsgEnd, (int)Winner.Seeker);
                    return;
                }
                if (Time.time >= _phaseEndTime)
                    Send(MsgEnd, (int)Winner.Hiders);
            }
            else if (Phase == Phase.Hiding && hiders.Count == 0 && !_roundTest)
            {
                Send(MsgEnd, (int)Winner.Aborted);
            }
        }

        private void HostKill(PlayerAvatar h)
        {
            int id = h.photonView.ViewID;
            if (_killCooldown.TryGetValue(id, out float until) && Time.time < until) return;
            _killCooldown[id] = Time.time + 2f;
            Send(MsgKill, id);
        }

        private void HostHandleAttack(Vector3 pos, Vector3 forward)
        {
            if (Phase != Phase.Hunting || Seeker == null) return;
            forward.y = 0;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Seeker.transform.forward;
            foreach (var h in AliveHiders())
            {
                Vector3 to = h.transform.position - pos;
                float dist = to.magnitude;
                if (dist > _roundAttackRange) continue;
                Vector3 flat = new Vector3(to.x, 0, to.z);
                if (dist > 1.2f && flat.sqrMagnitude > 0.001f && Vector3.Dot(flat.normalized, forward) < 0.35f) continue;
                HostKill(h);
            }

            if (!_roundTest) return;
            _attackCount++;
            TestDummy nearest = null;
            float nearestDist = float.MaxValue, nearestAngle = 0f;
            bool hit = false;
            foreach (var d in AliveDummies())
            {
                Vector3 to = d.transform.position - pos;
                Vector3 flat = new Vector3(to.x, 0, to.z);
                float dist = flat.magnitude;
                float angle = flat.sqrMagnitude > 0.001f ? Vector3.Angle(flat, forward) : 0f;
                if (dist < nearestDist) { nearestDist = dist; nearestAngle = angle; nearest = d; }
                if (dist > _roundAttackRange) continue;
                if (dist > 1.2f && Vector3.Dot(flat.normalized, forward) < 0.35f) continue;
                KillDummy(d, pos, "attack");
                _lastAttackInfo = L.AttackHit(d.DummyName, dist);
                hit = true;
            }
            if (!hit)
                _lastAttackInfo = nearest == null ? L.AttackMissNoTarget
                    : L.AttackMiss(nearest.DummyName, nearestDist, nearestAngle, _roundAttackRange);
        }

        // =====================================================================
        // Test mode
        // =====================================================================
        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private List<TestDummy> AliveDummies()
        {
            _dummies.RemoveAll(d => d == null);
            return _dummies.Where(d => !d.Dead).ToList();
        }

        private void KillDummy(TestDummy d, Vector3 from, string how)
        {
            if (d == null || d.Dead) return;
            d.Kill(from);
            _killCount++;
            Banner(L.Caught(d.DummyName), new Color(1f, 0.35f, 0.2f), 2f);
            if (_monster != null) _monster.PlayAttack();
            Plugin.Log.LogInfo($"[Test] {d.DummyName} killed by {how} at {FlatDistance(from, d.transform.position):0.0}m");
        }

        private void SpawnDummyRing(Vector3 center, int count, float radius)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                SpawnDummyAt(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
            }
        }

        private void SpawnDummyAt(Vector3 pos)
        {
            // snap to the floor
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point;
            _dummies.Add(TestDummy.Spawn(pos, ++_dummyCounter));
        }

        private void TestKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F9].wasPressedThisFrame) CycleMonster(kb[Key.LeftShift].isPressed ? -1 : 1);

            if (kb[Key.F10].wasPressedThisFrame && Seeker != null)
            {
                var cam = Camera.main != null ? Camera.main.transform : Seeker.transform;
                var f = cam.forward; f.y = 0;
                if (f.sqrMagnitude < 0.001f) f = Seeker.transform.forward;
                SpawnDummyAt(Seeker.transform.position + f.normalized * 4f);
            }

            if (kb[Key.F11].wasPressedThisFrame) _debugOverlay = !_debugOverlay;
        }

        private void CycleMonster(int dir)
        {
            var list = MonsterCatalog.Entries;
            if (Seeker == null || list.Count == 0) return;
            int idx = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].Name == _monsterName) idx = i;
            idx = ((idx + dir) % list.Count + list.Count) % list.Count;

            if (_monster != null) _monster.Remove();
            _monster = MonsterVisual.Create(Seeker, list[idx].Name, Plugin.MonsterScale.Value);
            _monsterName = _monster != null ? _monster.MonsterName : list[idx].Name;
            Banner(_monsterName, new Color(1f, 0.6f, 0.4f), 1.5f);
            if (_monster != null) _monster.PlayRoar();
        }

        private void ClearDummies()
        {
            foreach (var d in _dummies) if (d != null) Destroy(d.gameObject);
            _dummies.Clear();
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
            return list == null ? new List<PlayerAvatar>() : list.Where(p => p != null && p.photonView != null).ToList();
        }

        private static bool IsAlive(PlayerAvatar p) => p != null && !p.isDisabled && !p.deadSet;

        private List<PlayerAvatar> AliveHiders() =>
            AllPlayers().Where(p => _hiderViewIds.Contains(p.photonView.ViewID) && IsAlive(p)).ToList();

        private static PlayerAvatar FindByViewId(int id) =>
            AllPlayers().FirstOrDefault(p => p.photonView.ViewID == id);

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
                Handle(payload, -1);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCode) return;
            if (!(photonEvent.CustomData is object[] data)) return;
            Handle(data, photonEvent.Sender);
        }

        private static bool SenderIsMaster(int actor)
        {
            if (actor < 0 || PhotonNetwork.CurrentRoom == null) return true; // singleplayer
            var p = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            return p != null && p.IsMasterClient;
        }

        private bool SenderIsSeeker(int actor)
        {
            if (Seeker == null) return false;
            if (actor < 0 || PhotonNetwork.CurrentRoom == null) return true;
            return Seeker.photonView.OwnerActorNr == actor;
        }

        private void Handle(object[] data, int sender)
        {
            if (data.Length < 2 || !(data[0] is string m) || m != Magic) return;
            try
            {
                switch ((int)data[1])
                {
                    case MsgStart:
                        if (!SenderIsMaster(sender)) return;
                        OnStart((int)data[2], (float)data[3], (float)data[4], (float)data[5],
                            (bool)data[6], (bool)data[7], (bool)data[8], (float)data[9], (int[])data[10],
                            (string)data[11], (float)data[12]);
                        break;
                    case MsgKill:
                        if (!SenderIsMaster(sender)) return;
                        OnKill((int)data[2]);
                        break;
                    case MsgEnd:
                        if (!SenderIsMaster(sender)) return;
                        EndRoundLocal((Winner)(int)data[2], true);
                        break;
                    case MsgAttack:
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hunting) return;
                        if (_monster != null) _monster.PlayAttack();
                        if (IsHost) HostHandleAttack((Vector3)data[2], (Vector3)data[3]);
                        break;
                    case MsgRoar:
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hunting) return;
                        if (_monster != null) _monster.PlayRoar();
                        var local = PlayerAvatar.instance;
                        if (local != null && local != Seeker && Vector3.Distance(local.transform.position, (Vector3)data[2]) < 18f)
                            _flashUntil = Time.time + 0.6f;
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
            bool test = Plugin.TestMode.Value;
            var alive = AllPlayers().Where(IsAlive).ToList();
            if (alive.Count < 2 && SemiFunc.IsMultiplayer() && !test) return;
            if (alive.Count == 0) return;

            PlayerAvatar seeker = _selectedViewId >= 0 ? alive.FirstOrDefault(p => p.photonView.ViewID == _selectedViewId) : null;
            // In test mode "random" means: you are the monster.
            if (seeker == null && test) seeker = alive.FirstOrDefault(p => p.isLocal);
            if (seeker == null) seeker = alive[Random.Range(0, alive.Count)];

            string monster = _selectedMonster;
            var catalog = MonsterCatalog.Entries;
            if (monster == RandomMonster || MonsterCatalog.Find(monster) == null)
                monster = catalog.Count > 0 ? catalog[Random.Range(0, catalog.Count)].Name : "";

            int[] hiders = alive.Where(p => p != seeker).Select(p => p.photonView.ViewID).ToArray();

            _killCooldown.Clear();
            ClearDummies();
            _roundTest = test;
            _attackCount = _killCount = 0;
            _lastAttackInfo = "-";
            if (test) SpawnDummyRing(seeker.transform.position, Plugin.TestDummyCount.Value, 5f);

            Send(MsgStart,
                seeker.photonView.ViewID,
                test ? Mathf.Min(Plugin.HideTime.Value, 3f) : Plugin.HideTime.Value,
                Plugin.RoundTime.Value,
                Plugin.SeekerSpeed.Value,
                Plugin.SeekerInfiniteStamina.Value,
                Plugin.SeekerInvincible.Value,
                Plugin.BlindSeekerWhileHiding.Value,
                Plugin.MonsterScale.Value,
                hiders,
                monster,
                Plugin.AttackRange.Value);
            _menuOpen = false;
        }

        private void OnStart(int seekerId, float hide, float hunt, float speed, bool infStamina,
            bool invincible, bool blind, float scale, int[] hiders, string monster, float attackRange)
        {
            if (RoundActive) EndRoundLocal(Winner.Aborted, false);

            var seeker = FindByViewId(seekerId);
            if (seeker == null)
            {
                Plugin.Log.LogWarning("Seeker not found for view id " + seekerId);
                return;
            }

            Seeker = seeker;
            _roundHuntTime = hunt;
            _roundSpeed = speed;
            _roundInfStamina = infStamina;
            SeekerInvincibleThisRound = invincible;
            _roundBlind = blind;
            _roundAttackRange = attackRange;
            _hiderViewIds.Clear();
            foreach (var h in hiders) _hiderViewIds.Add(h);
            _thirdPerson = Plugin.ThirdPerson.Value;
            _attackReadyAt = _roarReadyAt = 0f;

            Phase = Phase.Hiding;
            _phaseEndTime = Time.time + hide;

            _monster = MonsterVisual.Create(seeker, monster, scale);
            _monsterName = _monster != null ? _monster.MonsterName : monster;

            Plugin.Log.LogInfo($"Round started. Seeker: {NameOf(seeker)} as '{_monsterName}', hiders: {hiders.Length}");
        }

        private void OnKill(int victimId)
        {
            var victim = FindByViewId(victimId);
            if (victim == null) return;
            Banner(L.Caught(NameOf(victim)), new Color(1f, 0.35f, 0.2f), 3f);
            if (_monster != null) _monster.PlayAttack();

            if (!victim.isLocal || !IsAlive(victim)) return;

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
            ThirdPersonCamera.Active = false;
            ThirdPersonCamera.RestoreAll();

            Phase = Phase.None;
            Seeker = null;
            _hiderViewIds.Clear();
            _killCooldown.Clear();
            SeekerInvincibleThisRound = false;
            ClearDummies();
            _roundTest = false;

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
                _smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold };
            }
            _bigStyle.fontSize = Mathf.RoundToInt(44 * s);
            _hudStyle.fontSize = Mathf.RoundToInt(24 * s);
            _smallStyle.fontSize = Mathf.RoundToInt(20 * s);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float sw = Screen.width, sh = Screen.height;
            float u = sh / 1080f;
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
                    ShadowLabel(new Rect(0, sh * 0.36f, sw, 80 * u), L.YouAreSeeker, _bigStyle);
                    GUI.color = new Color(1f, 0.6f, 0.5f);
                    ShadowLabel(new Rect(0, sh * 0.44f, sw, 60 * u), _monsterName, _hudStyle);
                    GUI.color = Color.white;
                    ShadowLabel(new Rect(0, sh * 0.5f, sw, 80 * u), L.WaitForHiders(secs), _bigStyle);
                }
                else if (localIsSeeker && Phase == Phase.Hunting)
                {
                    GUI.color = new Color(0.6f, 0f, 0f, 0.14f);
                    GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
                    GUI.color = new Color(1f, 0.85f, 0.85f);
                    ShadowLabel(new Rect(0, sh - 60 * u, sw, 40 * u),
                        L.SeekerControls(BindName(_attackAction), Cooldown(_attackReadyAt), BindName(_roarAction), Cooldown(_roarReadyAt), BindName(_viewAction)),
                        _smallStyle);
                }
                else if (Phase == Phase.Hiding)
                {
                    GUI.color = new Color(1f, 0.85f, 0.2f);
                    ShadowLabel(new Rect(0, sh * 0.15f, sw, 80 * u), L.HideNow(NameOf(Seeker), _monsterName, secs), _bigStyle);
                }

                if (!localIsSeeker && Time.time < _flashUntil)
                {
                    GUI.color = new Color(0.8f, 0f, 0f, 0.35f * ((_flashUntil - Time.time) / 0.6f));
                    GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
                }

                GUI.color = Color.white;
                string hud = Phase == Phase.Hiding ? L.Hiding(secs) : L.Hunt(secs, CountAliveHidersLocal());
                ShadowLabel(new Rect(0, 12, sw, 40 * u), hud, _hudStyle);
            }

            if (!string.IsNullOrEmpty(_bannerText) && Time.time < _bannerUntil)
            {
                GUI.color = _bannerColor;
                ShadowLabel(new Rect(0, sh * 0.62f, sw, 90 * u), _bannerText, _bigStyle);
            }
            GUI.color = Color.white;

            if (RoundActive && _roundTest && _debugOverlay) DrawDebugOverlay(u);

            if (_menuOpen)
                _window = GUILayout.Window(0x48A5, _window, DrawWindow, L.MenuTitle);
        }

        private void DrawDebugOverlay(float u)
        {
            if (_debugStyle == null)
                _debugStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
            _debugStyle.fontSize = Mathf.RoundToInt(16 * u);

            var cat = MonsterCatalog.Entries;
            int idx = -1;
            for (int i = 0; i < cat.Count; i++) if (cat[i].Name == _monsterName) idx = i;
            string text =
                $"{L.TestModeTitle}\n" +
                $"Monster: {_monsterName}  ({idx + 1}/{cat.Count})\n" +
                (_monster != null ? _monster.DebugInfo + "\n" : "") +
                $"Camera: {(ThirdPersonCamera.Active ? "third person" : "first person")}   Phase: {Phase}\n" +
                $"Dummies alive: {AliveDummies().Count}   Attacks: {_attackCount}   Kills: {_killCount}\n" +
                $"Last attack: {_lastAttackInfo}\n" +
                $"Attack range: {_roundAttackRange:0.0}m   Touch range: {Plugin.KillRange.Value:0.0}m\n\n" +
                L.TestKeys;

            var rect = new Rect(12, 60 * u, 560 * u, 420 * u);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(rect, _white);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12), text, _debugStyle);
        }

        private static string BindName(InputAction a)
        {
            try { return a.GetBindingDisplayString(); } catch { return "?"; }
        }

        private static string Cooldown(float readyAt)
        {
            float left = readyAt - Time.time;
            return left > 0 ? $" ({left:0.0}s)" : "";
        }

        private int CountAliveHidersLocal() =>
            AllPlayers().Count(p => _hiderViewIds.Contains(p.photonView.ViewID) && IsAlive(p)) + AliveDummies().Count;

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

            // --- Seeker ---
            GUILayout.Label(L.ChooseSeeker);
            var alive = AllPlayers().Where(IsAlive).ToList();
            if (_selectedViewId >= 0 && !alive.Any(p => p.photonView.ViewID == _selectedViewId)) _selectedViewId = -1;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(130));
            if (GUILayout.Toggle(_selectedViewId < 0, "  " + L.Random)) _selectedViewId = -1;
            foreach (var p in alive)
            {
                int vid = p.photonView.ViewID;
                bool sel = _selectedViewId == vid;
                if (GUILayout.Toggle(sel, "  " + NameOf(p)) && !sel) _selectedViewId = vid;
            }
            GUILayout.EndScrollView();

            // --- Monster ---
            GUILayout.Label(L.ChooseMonster);
            _scrollMonsters = GUILayout.BeginScrollView(_scrollMonsters, GUILayout.Height(130));
            if (GUILayout.Toggle(_selectedMonster == RandomMonster, "  " + L.Random)) _selectedMonster = RandomMonster;
            foreach (var e in MonsterCatalog.Entries)
            {
                bool sel = _selectedMonster == e.Name;
                if (GUILayout.Toggle(sel, "  " + e.Name) && !sel) _selectedMonster = e.Name;
            }
            if (MonsterCatalog.Entries.Count == 0) GUILayout.Label(L.NoMonsters);
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            Plugin.HideTime.Value = Slider(L.HideTime, Plugin.HideTime.Value, 5f, 120f, "s", 0);
            Plugin.RoundTime.Value = Slider(L.RoundTime, Plugin.RoundTime.Value, 30f, 900f, "s", 0);
            Plugin.AttackRange.Value = Slider(L.AttackRange, Plugin.AttackRange.Value, 1f, 5f, "m", 1);
            Plugin.SeekerSpeed.Value = Slider(L.SeekerSpeed, Plugin.SeekerSpeed.Value, 0.5f, 2.5f, "x", 2);
            Plugin.MonsterScale.Value = Slider(L.MonsterSize, Plugin.MonsterScale.Value, 0.5f, 2f, "x", 2);
            Plugin.DisableEnemies.Value = GUILayout.Toggle(Plugin.DisableEnemies.Value, " " + L.DisableEnemies);
            Plugin.SeekerInvincible.Value = GUILayout.Toggle(Plugin.SeekerInvincible.Value, " " + L.Invincible);
            Plugin.BlindSeekerWhileHiding.Value = GUILayout.Toggle(Plugin.BlindSeekerWhileHiding.Value, " " + L.Blind);
            GUILayout.Space(4);
            Plugin.TestMode.Value = GUILayout.Toggle(Plugin.TestMode.Value, " " + L.TestMode);
            if (Plugin.TestMode.Value) GUILayout.Label(L.TestModeHint);

            GUILayout.Space(8);
            bool enough = alive.Count >= 2 || !SemiFunc.IsMultiplayer() || Plugin.TestMode.Value;
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
