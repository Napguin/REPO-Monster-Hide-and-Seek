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
        Aborted = 2,
        MonsterDied = 3
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
        private const int MsgStart = 1, MsgKill = 2, MsgEnd = 3, MsgAttack = 4, MsgRoar = 5, MsgLaser = 6, MsgAim = 7, MsgChoose = 8, MsgScore = 9;
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
        private InputAction _attackAction, _roarAction, _viewAction, _specialAction;
        private float _specialReadyAt;

        // ---- seeker picks own monster ----
        private bool _roundSeekerChooses;
        private bool _choiceOpen;
        private float _roundScale = 1f;
        private Vector2 _choiceScroll;
        private Rect _choiceWindow = new Rect(0, 0, 420, 560);

        // ---- laser (all clients) ----
        private float _laserChargeUntil = -1f, _laserEndTime = -1f;
        private Vector3 _laserAim = Vector3.forward;
        private float _aimSendTimer;
        private const float LaserCharge = 0.6f;

        // ---- test mode (host-local) ----
        private bool _roundTest;
        private readonly List<TestDummy> _dummies = new List<TestDummy>();
        private int _dummyCounter;
        private bool _debugOverlay = true;
        private int _rawAttackPresses, _rawRoarPresses;
        private string _lastError = "none";
        private string _lastAttackInfo = "-";
        private int _attackCount, _killCount;
        private GUIStyle _debugStyle;

        // ---- scoring ----
        // host: elimination order of this round + seeker kills; totals over the whole session
        private readonly Dictionary<int, float> _hiderOutTime = new Dictionary<int, float>();
        private readonly Dictionary<int, string> _roundNames = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _roundKeys = new Dictionary<int, string>();
        private readonly HashSet<int> _seekerKilled = new HashSet<int>();
        private int _roundSeekerId = -1;
        private readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _totalNames = new Dictionary<string, string>();
        // all clients: last scoreboard received
        private string[] _sbNames = new string[0];
        private int[] _sbRound = new int[0], _sbTotal = new int[0];
        private bool[] _sbSeeker = new bool[0];
        private float _resultsUntil;
        private GUIStyle _scoreStyle;

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
            try { _roarAction.Disable(); _roarAction.AddBinding("<Keyboard>/r"); _roarAction.Enable(); } catch { /* optional */ }
            _viewAction = MakeAction(Plugin.ToggleViewBinding.Value, "<Keyboard>/v");
            _specialAction = MakeAction(Plugin.SpecialBinding.Value, "<Mouse>/middleButton");
            try { _specialAction.Disable(); _specialAction.AddBinding("<Keyboard>/f"); _specialAction.Enable(); } catch { /* optional */ }
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

            bool choiceVisible = _choiceOpen && RoundActive && Phase == Phase.Hiding && Seeker != null && GameRefs.IsLocal(Seeker);
            if (_menuOpen || choiceVisible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Compat.Call(Compat.Static("CursorManager", "instance"), "Unlock", 0.2f);
                Compat.Call(PlayerController.instance, "InputDisable", 0.2f);
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

            Safe("input", () =>
            {
                if (_attackAction.WasPressedThisFrame()) _rawAttackPresses++;
                if (_roarAction.WasPressedThisFrame()) _rawRoarPresses++;
            });
            if (_roundTest && !_menuOpen) Safe("test keys", TestKeys);
            Safe("seeker", LocalSeekerTick);
            Safe("laser", LaserTick);
            if (IsHost) Safe("host", HostTick);
        }

        private void LocalSeekerTick()
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            ThirdPersonCamera.Active = localSeeker && Phase == Phase.Hunting && _thirdPerson;
            float h = _monster != null ? _monster.BodyHeight : 2f;
            ThirdPersonCamera.Distance = Mathf.Max(Plugin.CameraDistance.Value, h * 1.1f + 1.5f);
            ThirdPersonCamera.Height = Mathf.Clamp(h - 1.3f, 0.6f, 3f);
            if (!localSeeker) return;

            // Own monster body only when the third-person camera really works – otherwise it would block the view.
            if (_monster != null) _monster.SetBodyVisible(ThirdPersonCamera.Working);

            // Each part separately, so a changed game method can't take the others down.
            // Doors, buttons, carts ... are all "grabbed" in R.E.P.O., so grabbing stays on unless disabled in the config.
            if (!Plugin.MonsterCanGrab.Value)
                Safe("grab", () => Compat.Set(Compat.Static("PhysGrabber", "instance"), "grabDisableTimer", 0.25f));

            var pc = PlayerController.instance;
            if (Phase == Phase.Hiding)
            {
                Safe("freeze", () => Compat.Call(pc, "InputDisable", 0.2f)); // frozen while the others hide
                return;
            }

            Safe("speed", () =>
            {
                float speed = _roundSpeed * (Time.time < _lungeUntil ? 1.8f : 1f);
                if (!Mathf.Approximately(speed, 1f)) Compat.Call(pc, "OverrideSpeed", speed, 0.2f);
            });
            if (_roundInfStamina)
                Safe("stamina", () => Compat.Set(pc, "EnergyCurrent", Compat.Get(pc, "EnergyStart", 100f)));

            if (_menuOpen || GameRefs.IsTyping) return;

            Safe("view key", () =>
            {
                if (!_viewAction.WasPressedThisFrame()) return;
                _thirdPerson = !_thirdPerson;
                Plugin.Log.LogInfo($"[Monster] view -> {(_thirdPerson ? "third" : "first")} person (camera: {ThirdPersonCamera.MainCameraName})");
            });

            Safe("attack key", () =>
            {
                if (!_attackAction.WasPressedThisFrame() || Time.time < _attackReadyAt) return;
                _attackReadyAt = Time.time + Plugin.AttackCooldown.Value;
                _lungeUntil = Time.time + 0.35f;
                var cam = GameRefs.ViewTransform(Seeker);
                Plugin.Log.LogInfo("[Monster] attack pressed");
                Send(MsgAttack, SeekerPosition(), cam != null ? cam.forward : Seeker.transform.forward);
            });

            Safe("roar key", () =>
            {
                if (!_roarAction.WasPressedThisFrame() || Time.time < _roarReadyAt) return;
                _roarReadyAt = Time.time + Plugin.RoarCooldown.Value;
                Plugin.Log.LogInfo("[Monster] roar pressed");
                Banner(L.RoarBanner, new Color(1f, 0.3f, 0.2f), 1f);
                Send(MsgRoar, SeekerPosition());
            });

            Safe("special key", () =>
            {
                if (!_specialAction.WasPressedThisFrame()) return;
                if (_monster == null || !_monster.HasLaser) { Banner(L.NoSpecial, Color.white, 1.5f); return; }
                if (Time.time < _specialReadyAt) return;
                _specialReadyAt = Time.time + Plugin.LaserCooldown.Value + Plugin.LaserDuration.Value;
                Plugin.Log.LogInfo("[Monster] laser pressed");
                Send(MsgLaser, LocalAimDirection());
            });

            // Keep everybody updated where the laser points while it fires.
            if (LaserActive())
            {
                _laserAim = LocalAimDirection();
                _aimSendTimer -= Time.deltaTime;
                if (_aimSendTimer <= 0f)
                {
                    _aimSendTimer = 0.1f;
                    Safe("aim", () => SendUnreliable(MsgAim, _laserAim));
                }
            }
        }

        // =====================================================================
        // Laser
        // =====================================================================
        private bool LaserActive() => Time.time < _laserEndTime;

        /// <summary>Direction from the monster's muzzle to what the player's camera looks at.</summary>
        private Vector3 LocalAimDirection()
        {
            var view = GameRefs.ViewTransform(Seeker);
            if (view == null) return Seeker != null ? Seeker.transform.forward : Vector3.forward;
            Vector3 muzzle = _monster != null ? _monster.MuzzlePosition : view.position;
            Vector3 target = view.position + view.forward * 40f;
            if (Physics.Raycast(view.position, view.forward, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                && !IsPlayerCollider(hit.collider))
                target = hit.point;
            var dir = target - muzzle;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : view.forward;
        }

        private static bool IsPlayerCollider(Collider c) =>
            c != null && (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<PlayerController>() != null
                          || c.GetComponentInParent<MonsterVisual>() != null);

        private void LaserTick()
        {
            if (_monster == null) return;
            if (!LaserActive())
            {
                if (_laserEndTime > 0f) { _monster.LaserStop(); _laserEndTime = -1f; }
                return;
            }
            if (Time.time < _laserChargeUntil) return; // charging

            Vector3 start = _monster.MuzzlePosition;
            Vector3 dir = _laserAim.sqrMagnitude > 0.01f ? _laserAim.normalized : _monster.transform.forward;
            float range = 30f;
            Vector3 end = start + dir * range;
            bool hitting = false;
            var hits = Physics.RaycastAll(start, dir, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            foreach (var h in hits)
            {
                if (IsPlayerCollider(h.collider)) continue; // the beam goes through players (and kills them)
                if (h.distance < best) { best = h.distance; end = h.point; hitting = true; }
            }
            _monster.LaserFrame(start, end, hitting);

            if (IsHost) HostLaserHits(start, end);
        }

        private void HostLaserHits(Vector3 a, Vector3 b)
        {
            const float radius = 0.8f;
            foreach (var h in AliveHiders())
                if (DistanceToSegment(PlayerPos(h) + Vector3.up * 0.8f, a, b) < radius) HostKill(h);
            foreach (var d in AliveDummies())
                if (DistanceToSegment(d.transform.position + Vector3.up * 0.9f, a, b) < radius)
                {
                    KillDummy(d, a, "laser");
                    _lastAttackInfo = L.LaserHit(d.DummyName);
                }
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

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
            if (!_roundTest) RecordEliminations(hiders);

            // The monster died (invincibility off, or a death path we couldn't block): the hiders win.
            if (!IsAlive(Seeker))
            {
                Plugin.Log.LogInfo("[Round] monster died – hiders win");
                HostSendScores();
                Send(MsgEnd, (int)Winner.MonsterDied);
                return;
            }

            if (Phase == Phase.Hunting)
            {
                // Touching a hider also kills.
                Vector3 sp = SeekerPosition();
                foreach (var h in hiders)
                    if (FlatDistance(sp, PlayerPos(h)) <= Plugin.KillRange.Value && Mathf.Abs(PlayerPos(h).y - sp.y) < 2f)
                        HostKill(h);
                foreach (var d in AliveDummies())
                    if (FlatDistance(sp, d.transform.position) <= Plugin.KillRange.Value)
                        KillDummy(d, sp, "touch");

                // Test mode keeps running so you can respawn dummies (F10) and try more monsters.
                if (hiders.Count == 0 && AliveDummies().Count == 0 && !_roundTest)
                {
                    HostSendScores();
                    Send(MsgEnd, (int)Winner.Seeker);
                    return;
                }
                if (Time.time >= _phaseEndTime)
                {
                    HostSendScores();
                    Send(MsgEnd, (int)Winner.Hiders);
                }
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
            if (!_roundTest) _seekerKilled.Add(id); // 1 point per hider, counted once
            Send(MsgKill, id);
        }

        // =====================================================================
        // Scoring (host)
        // =====================================================================
        private static string PlayerKey(PlayerAvatar p)
        {
            var id = Compat.Get<string>(p, "steamID");
            return string.IsNullOrEmpty(id) ? "name:" + NameOf(p) : id;
        }

        private void HostBeginScoring(PlayerAvatar seeker, IEnumerable<PlayerAvatar> hiders)
        {
            _hiderOutTime.Clear();
            _roundNames.Clear();
            _roundKeys.Clear();
            _seekerKilled.Clear();
            _roundSeekerId = seeker.photonView.ViewID;
            foreach (var p in hiders.Concat(new[] { seeker }))
            {
                int id = p.photonView.ViewID;
                _roundNames[id] = NameOf(p);
                _roundKeys[id] = PlayerKey(p);
            }
        }

        /// <summary>Remembers when each hider was eliminated (eaten, or died any other way / left).</summary>
        private void RecordEliminations(List<PlayerAvatar> aliveHiders)
        {
            foreach (int id in _hiderViewIds)
            {
                if (_hiderOutTime.ContainsKey(id)) continue;
                if (aliveHiders.Any(p => p.photonView.ViewID == id)) continue;
                _hiderOutTime[id] = Time.time;
            }
        }

        /// <summary>
        /// Hiders: the one who lasts longest gets as many points as there were hiders, the next one 1 less, ...
        /// whoever was eliminated first gets 0. Everyone still alive at the end shares the top score.
        /// Seeker: 1 point per hider eaten.
        /// </summary>
        private void HostSendScores()
        {
            if (_roundTest || _roundSeekerId < 0) return;
            RecordEliminations(AliveHiders());

            int h = _hiderViewIds.Count;
            var round = new Dictionary<int, int>();
            float firstOut = _hiderOutTime.Count > 0 ? _hiderOutTime.Values.Min() : float.MaxValue;
            foreach (int id in _hiderViewIds)
            {
                bool survived = !_hiderOutTime.TryGetValue(id, out float t);
                if (survived) { round[id] = h; continue; }
                if (Mathf.Approximately(t, firstOut)) { round[id] = 0; continue; }
                int outlasting = _hiderViewIds.Count(o => o != id && (!_hiderOutTime.TryGetValue(o, out float ot) || ot > t));
                round[id] = Mathf.Max(0, h - outlasting);
            }
            round[_roundSeekerId] = _seekerKilled.Count;

            foreach (var kv in round)
            {
                if (!_roundKeys.TryGetValue(kv.Key, out var key)) continue;
                _totals.TryGetValue(key, out int tot);
                _totals[key] = tot + kv.Value;
                _totalNames[key] = _roundNames[kv.Key];
            }

            // everyone who ever scored, sorted by total
            var keys = _totals.Keys.OrderByDescending(k => _totals[k]).ThenBy(k => _totalNames[k]).ToList();
            var keyToId = _roundKeys.ToDictionary(kv => kv.Value, kv => kv.Key);
            string[] names = keys.Select(k => _totalNames[k]).ToArray();
            int[] roundPts = keys.Select(k => keyToId.TryGetValue(k, out int id) && round.TryGetValue(id, out int r) ? r : 0).ToArray();
            int[] totals = keys.Select(k => _totals[k]).ToArray();
            bool[] seeker = keys.Select(k => keyToId.TryGetValue(k, out int id) && id == _roundSeekerId).ToArray();
            Plugin.Log.LogInfo("[Score] " + string.Join(", ", names.Select((n, i) => $"{n} +{roundPts[i]} = {totals[i]}")));
            Send(MsgScore, names, roundPts, totals, seeker, true);
            _roundSeekerId = -1;
        }

        private void HostResetScores()
        {
            _totals.Clear();
            _totalNames.Clear();
            Send(MsgScore, new string[0], new int[0], new int[0], new bool[0], false);
        }

        private void HostHandleAttack(Vector3 pos, Vector3 forward)
        {
            if (Phase != Phase.Hunting || Seeker == null) return;
            forward.y = 0;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Seeker.transform.forward;
            foreach (var hd in AliveHiders())
                Plugin.Log.LogInfo($"[Monster] attack check: {NameOf(hd)} at {FlatDistance(PlayerPos(hd), pos):0.0}m, height diff {PlayerPos(hd).y - pos.y:0.0}m (range {_roundAttackRange:0.0}m)");
            foreach (var h in AliveHiders())
            {
                Vector3 to = PlayerPos(h) - pos;
                Vector3 flat = new Vector3(to.x, 0, to.z);
                float dist = flat.magnitude;
                if (dist > _roundAttackRange || Mathf.Abs(to.y) > 2.5f) continue;
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

            if (kb[Key.F9].wasPressedThisFrame)
            {
                Plugin.Log.LogInfo("[Test] F9 pressed");
                CycleMonster(kb[Key.LeftShift].isPressed ? -1 : 1);
            }

            if (kb[Key.F10].wasPressedThisFrame && Seeker != null)
            {
                var cam = GameRefs.ViewTransform(Seeker);
                var f = cam != null ? cam.forward : Seeker.transform.forward; f.y = 0;
                if (f.sqrMagnitude < 0.001f) f = Seeker.transform.forward;
                SpawnDummyAt(SeekerPosition() + f.normalized * 4f);
                Plugin.Log.LogInfo("[Test] F10 dummy spawned");
            }

            if (kb[Key.F11].wasPressedThisFrame) _debugOverlay = !_debugOverlay;
        }

        private void CycleMonster(int dir)
        {
            var list = MonsterCatalog.Entries;
            if (Seeker == null || list.Count == 0)
            {
                Plugin.Log.LogWarning($"[Test] cannot cycle: seeker {(Seeker == null ? "missing" : "ok")}, catalog {list.Count} monsters");
                Banner(L.NoMonsters, Color.white, 2f);
                return;
            }
            int idx = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].Name == _monsterName) idx = i;
            idx = ((idx + dir) % list.Count + list.Count) % list.Count;

            SwitchMonster(list[idx].Name);
        }

        /// <summary>Rebuilds the seeker's monster body (local).</summary>
        private void SwitchMonster(string name)
        {
            if (Seeker == null) return;
            _laserEndTime = -1f;
            if (_monster != null) _monster.Remove();
            _monster = MonsterVisual.Create(Seeker, name, _roundScale);
            _monsterName = _monster != null ? _monster.MonsterName : name;
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
                    if (Compat.Get(e, "Spawned", false)) Compat.Call(e, "Despawn");
                    if (Compat.Get(e, "DespawnedTimer", 0f) < 30f) Compat.Set(e, "DespawnedTimer", 30f);
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
        private readonly Dictionary<string, float> _errorLogged = new Dictionary<string, float>();

        /// <summary>Runs one part of the update; an error there is logged (max every 5 s) instead of breaking everything else.</summary>
        private void Safe(string part, System.Action action)
        {
            try { action(); }
            catch (System.Exception ex)
            {
                if (!_errorLogged.TryGetValue(part, out float t) || Time.time - t > 5f)
                {
                    _errorLogged[part] = Time.time;
                    Plugin.Log.LogError($"[{part}] {ex}");
                    _lastError = $"{part}: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        /// <summary>Where the seeker really is (for the local seeker the controller is the truth).</summary>
        private Vector3 SeekerPosition()
        {
            if (Seeker == null) return Vector3.zero;
            if (GameRefs.IsLocal(Seeker) && PlayerController.instance != null)
            {
                var p = PlayerController.instance.transform.position;
                // controller origin may sit inside the capsule: use the floor below it
                if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerController>() == null)
                    return hit.point;
                return p;
            }
            return PlayerPos(Seeker);
        }

        /// <summary>
        /// Real position of a player. For other players R.E.P.O. moves the networked client position,
        /// the avatar's root transform can lag behind or stay put.
        /// </summary>
        private static Vector3 PlayerPos(PlayerAvatar p)
        {
            if (p == null) return Vector3.zero;
            if (GameRefs.IsLocal(p) && PlayerController.instance != null) return PlayerController.instance.transform.position;
            var v = Compat.Get(p, "clientPositionCurrent", Vector3.zero);
            if (v == Vector3.zero) v = Compat.Get(p, "clientPosition", Vector3.zero);
            if (v != Vector3.zero) return v;
            if (p.playerAvatarVisuals != null) return p.playerAvatarVisuals.transform.position;
            return p.transform.position;
        }

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

        private void SendUnreliable(int type, params object[] args)
        {
            if (!(SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom)) return; // local client already knows
            var payload = new object[args.Length + 2];
            payload[0] = Magic;
            payload[1] = type;
            args.CopyTo(payload, 2);
            PhotonNetwork.RaiseEvent(EventCode, payload,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendUnreliable);
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
            var pv = Seeker.photonView;
            if (pv.OwnerActorNr == actor || pv.CreatorActorNr == actor) return true;
            if (pv.Owner != null && pv.Owner.ActorNumber == actor) return true;
            return pv.ViewID / PhotonNetwork.MAX_VIEW_IDS == actor; // ids are actorNr * 1000 + n
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
                            (string)data[11], (float)data[12], data.Length > 13 && (bool)data[13]);
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
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hunting)
                        {
                            Plugin.Log.LogInfo($"[Monster] attack ignored (sender {sender} is seeker: {SenderIsSeeker(sender)}, phase {Phase})");
                            return;
                        }
                        if (_monster != null) _monster.PlayAttack();
                        if (IsHost) HostHandleAttack((Vector3)data[2], (Vector3)data[3]);
                        break;
                    case MsgLaser:
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hunting) return;
                        _laserAim = (Vector3)data[2];
                        _laserChargeUntil = Time.time + LaserCharge;
                        _laserEndTime = _laserChargeUntil + Plugin.LaserDuration.Value;
                        if (_monster != null) _monster.PlayRoar();
                        Plugin.Log.LogInfo("[Monster] laser fired");
                        break;
                    case MsgScore:
                        if (!SenderIsMaster(sender)) return;
                        _sbNames = (string[])data[2];
                        _sbRound = (int[])data[3];
                        _sbTotal = (int[])data[4];
                        _sbSeeker = (bool[])data[5];
                        _resultsUntil = (bool)data[6] ? Time.time + 10f : 0f;
                        break;
                    case MsgChoose:
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hiding || !_roundSeekerChooses) return;
                        var chosen = (string)data[2];
                        if (MonsterCatalog.Find(chosen) == null) return;
                        SwitchMonster(chosen);
                        Plugin.Log.LogInfo($"[Monster] seeker chose '{chosen}'");
                        break;
                    case MsgAim:
                        if (!SenderIsSeeker(sender)) return;
                        if (Seeker != null && !GameRefs.IsLocal(Seeker)) _laserAim = (Vector3)data[2];
                        break;
                    case MsgRoar:
                        if (!SenderIsSeeker(sender) || Phase != Phase.Hunting) return;
                        if (_monster != null)
                        {
                            _monster.PlayRoar();
                            Plugin.Log.LogInfo("[Monster] roar -> " + _monster.LastSoundInfo);
                        }
                        var local = GameRefs.LocalAvatar;
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
            if (!test) HostBeginScoring(seeker, alive.Where(p => p != seeker));
            if (test)
            {
                Vector3 c = GameRefs.IsLocal(seeker) && PlayerController.instance != null ? PlayerController.instance.transform.position : seeker.transform.position;
                SpawnDummyRing(c, Plugin.TestDummyCount.Value, 5f);
                Plugin.Log.LogInfo($"[Test] round start: {_dummies.Count} dummies around {c}");
            }

            Send(MsgStart,
                seeker.photonView.ViewID,
                test ? Mathf.Min(Plugin.HideTime.Value, Plugin.SeekerChoosesMonster.Value ? 15f : 3f) : Plugin.HideTime.Value,
                Plugin.RoundTime.Value,
                Plugin.SeekerSpeed.Value,
                Plugin.SeekerInfiniteStamina.Value,
                Plugin.SeekerInvincible.Value,
                Plugin.BlindSeekerWhileHiding.Value,
                Plugin.MonsterScale.Value,
                hiders,
                monster,
                Plugin.AttackRange.Value,
                Plugin.SeekerChoosesMonster.Value);
            _menuOpen = false;
        }

        private void OnStart(int seekerId, float hide, float hunt, float speed, bool infStamina,
            bool invincible, bool blind, float scale, int[] hiders, string monster, float attackRange, bool seekerChooses)
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
            _roundScale = scale;
            _roundSeekerChooses = seekerChooses;
            _choiceOpen = seekerChooses && GameRefs.IsLocal(seeker);
            _specialReadyAt = 0f;
            _laserEndTime = -1f;
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
                    Compat.Set(health, "godMode", false);
                    Compat.Set(health, "invincibleTimer", 0f);
                    int dmg = Mathf.Max(Compat.Get(health, "health", 100), Compat.Get(health, "maxHealth", 100)) + 999;
                    Compat.Call(health, "Hurt", dmg, false);
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
                if (!Compat.Call(victim, "PlayerDeath", -1))
                    Plugin.Log.LogError("PlayerDeath could not be called in this game version");
            }
        }

        private void EndRoundLocal(Winner winner, bool showBanner)
        {
            if (!RoundActive) return;
            string seekerName = NameOf(Seeker);

            _laserEndTime = -1f;
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
                case Winner.MonsterDied: Banner(L.MonsterDied(seekerName), new Color(0.3f, 1f, 0.4f), 8f); break;
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
            bool localIsSeeker = RoundActive && Seeker != null && GameRefs.IsLocal(Seeker);

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
                        L.SeekerControls(BindName(_attackAction), Cooldown(_attackReadyAt), BindName(_roarAction), Cooldown(_roarReadyAt), BindName(_viewAction))
                        + (_monster != null && _monster.HasLaser ? L.LaserControl(BindName(_specialAction), Cooldown(_specialReadyAt)) : ""),
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

            if (RoundActive && _roundTest && _debugOverlay)
            {
                try { DrawDebugOverlay(u); }
                catch (System.Exception ex) { _lastError = "overlay: " + ex.Message; }
            }

            if (Time.time < _resultsUntil && _sbNames.Length > 0) DrawResults(sw, sh, u);

            if (_choiceOpen && RoundActive && Phase == Phase.Hiding && localIsSeeker)
            {
                GUI.color = Color.white;
                _choiceWindow.width = 420 * Mathf.Max(1f, u);
                _choiceWindow.height = 560 * Mathf.Max(1f, u);
                _choiceWindow.x = (sw - _choiceWindow.width) / 2f;
                _choiceWindow.y = (sh - _choiceWindow.height) / 2f;
                GUILayout.Window(0x48A6, _choiceWindow, DrawChoiceWindow, L.ChooseYourMonster);
            }

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
                $"Camera: {(ThirdPersonCamera.Active ? (ThirdPersonCamera.Working ? "third person" : "third person NOT WORKING") : "first person")}   Phase: {Phase}\n" +
                $"Last sound: {(_monster != null ? _monster.LastSoundInfo : "-")}\n" +
                $"Dummies alive: {AliveDummies().Count}   Attacks: {_attackCount}   Kills: {_killCount}\n" +
                $"Last attack: {_lastAttackInfo}\n" +
                $"Attack range: {_roundAttackRange:0.0}m   Touch range: {Plugin.KillRange.Value:0.0}m\n" +
                $"[Diag] local seeker: {(Seeker != null && GameRefs.IsLocal(Seeker))}  isLocal: {Seeker?.isLocal}  " +
                $"typing: {GameRefs.IsTyping}  camera: {ThirdPersonCamera.MainCameraName} ({ThirdPersonCamera.GameCameraCount} cams) " +
                $"(moved {ThirdPersonCamera.CamerasMoved}, last frame {Time.frameCount - ThirdPersonCamera.AppliedFrame} ago)\n" +
                $"[Diag] clicks L/R: {_rawAttackPresses}/{_rawRoarPresses}  pos: {SeekerPosition():F1}\n" +
                $"[Diag] last error: {_lastError}\n\n" +
                L.TestKeys;

            var rect = new Rect(12, 60 * u, 640 * u, 500 * u);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(rect, _white);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12), text, _debugStyle);
        }

        private void DrawResults(float sw, float sh, float u)
        {
            if (_scoreStyle == null)
                _scoreStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _scoreStyle.fontSize = Mathf.RoundToInt(22 * u);
            float rowH = 34 * u, w = 560 * u;
            float h = (2 + _sbNames.Length) * rowH + 20 * u;
            var box = new Rect((sw - w) / 2f, sh * 0.12f, w, h);
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(box, _white);
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(box.x + 16 * u, box.y + 8 * u, w, rowH), L.ScoreTitle, _scoreStyle);
            for (int i = 0; i < _sbNames.Length; i++)
            {
                float y = box.y + 8 * u + (i + 1.3f) * rowH;
                bool seeker = i < _sbSeeker.Length && _sbSeeker[i];
                GUI.color = seeker ? new Color(1f, 0.45f, 0.4f) : Color.white;
                GUI.Label(new Rect(box.x + 16 * u, y, w * 0.6f, rowH), $"{i + 1}. {_sbNames[i]}{(seeker ? "  (" + L.SeekerWord + ")" : "")}", _scoreStyle);
                GUI.color = new Color(0.6f, 1f, 0.6f);
                GUI.Label(new Rect(box.x + w * 0.62f, y, w * 0.18f, rowH), "+" + (i < _sbRound.Length ? _sbRound[i] : 0), _scoreStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(box.x + w * 0.8f, y, w * 0.2f, rowH), (i < _sbTotal.Length ? _sbTotal[i] : 0).ToString(), _scoreStyle);
            }
            GUI.color = Color.white;
        }

        private void DrawScoreList(bool hostControls)
        {
            GUILayout.Space(6);
            GUILayout.Label(L.ScoreTitle);
            if (_sbNames.Length == 0) GUILayout.Label("  " + L.NoScores);
            for (int i = 0; i < _sbNames.Length; i++)
                GUILayout.Label($"  {i + 1}. {_sbNames[i]}: {(i < _sbTotal.Length ? _sbTotal[i] : 0)}");
            if (hostControls && _sbNames.Length > 0 && GUILayout.Button(L.ResetScores)) HostResetScores();
        }

        private void DrawChoiceWindow(int id)
        {
            int secs = Mathf.Max(0, Mathf.CeilToInt(_phaseEndTime - Time.time));
            GUILayout.Label(L.ChoiceHint(secs));
            GUILayout.Label(L.Current(_monsterName));
            _choiceScroll = GUILayout.BeginScrollView(_choiceScroll);
            foreach (var e in MonsterCatalog.Entries)
            {
                bool sel = e.Name == _monsterName;
                string label = (sel ? "> " : "   ") + e.Name + (e.HasLaser ? "  (Laser)" : "");
                if (GUILayout.Button(label, GUILayout.Height(28)) && !sel)
                    Send(MsgChoose, e.Name);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(L.Done, GUILayout.Height(34))) _choiceOpen = false;
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
                DrawScoreList(false);
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
                if (GUILayout.Toggle(sel, "  " + e.Name + (e.HasLaser ? "  (Laser)" : "")) && !sel) _selectedMonster = e.Name;
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
            Plugin.SeekerChoosesMonster.Value = GUILayout.Toggle(Plugin.SeekerChoosesMonster.Value, " " + L.SeekerChooses);
            Plugin.TestMode.Value = GUILayout.Toggle(Plugin.TestMode.Value, " " + L.TestMode);
            if (Plugin.TestMode.Value) GUILayout.Label(L.TestModeHint);

            GUILayout.Space(8);
            DrawScoreList(true);

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
