using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>Special abilities of the monsters (everything except the laser, which has its own code).</summary>
    public partial class HideAndSeekManager
    {
        private const int MsgSpecial = 10, MsgDamage = 11;

        // local seeker
        private float _revealUntil;
        private readonly List<(Vector3 pos, string name)> _radarTargets = new List<(Vector3, string)>();
        // all clients: Rapid Charge / Flying Charge
        private float _chargeUntil;
        private AbilityType _chargeType;
        private bool Charging => Time.time < _chargeUntil;
        // Flying Grab / Zero-G Grab (all clients)
        private float _grabUntil;
        private AbilityType _grabType;
        private bool Grabbing => Time.time < _grabUntil;
        /// <summary>Main attack disabled while a charge or grab zone is active.</summary>
        private bool AttackBlocked => Charging || Grabbing || Time.time < _dashEnd;
        private float AttackBlockedUntil => Mathf.Max(Mathf.Max(Charging ? _chargeUntil : 0f, Grabbing ? _grabUntil : 0f), _dashEnd);
        private bool _grabStunShown, _grabCaught;
        // Charge / Lunge (all clients): wind-up, then dash
        private float _dashStart, _dashEnd;
        private AbilityType _dashType;
        private Vector3 _dashDir;
        private readonly HashSet<int> _dashHit = new HashSet<int>();
        private bool DashWindup => Time.time < _dashStart;
        private bool Dashing => Time.time >= _dashStart && Time.time < _dashEnd;
        // host: damage ticks
        private float _chargeTickAt, _laserTickAt;
        private readonly HashSet<int> _leapHit = new HashSet<int>();
        // all clients
        private float _leapUntil;
        private float _stunUntil;
        private readonly List<Projectile> _projectiles = new List<Projectile>();

        // Player scan (roar): only tells the monster HOW MANY players are close, not where.
        private const float ScanRange = 15f;

        private void OnRoarScan(Vector3 roarPos)
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            Effects.Shockwave(roarPos + Vector3.up * 0.1f, ScanRange * 2f, new Color(1f, 0.6f, 0.3f), 0.9f);
            if (localSeeker)
            {
                int n = AliveHiders().Count(h => FlatDistance(PlayerPos(h), roarPos) <= ScanRange)
                        + AliveDummies().Count(d => FlatDistance(d.transform.position, roarPos) <= ScanRange);
                Banner(n > 0 ? L.ScanFound(n) : L.ScanNothing, new Color(1f, 0.6f, 0.3f), 2.5f);
                Plugin.Log.LogInfo($"[Monster] scan: {n} player(s) within {ScanRange}m");
                return;
            }
            var local = GameRefs.LocalAvatar;
            if (local != null && IsAlive(local) && FlatDistance(PlayerPos(local), roarPos) <= ScanRange)
            {
                _flashUntil = Time.time + 0.6f;
                Banner(L.Scanned, new Color(1f, 0.5f, 0.3f), 2f);
            }
        }

        // =====================================================================
        // Damage (host decides, the victim applies it to its own health)
        // =====================================================================
        private readonly Dictionary<int, int> _hp = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _lastMonsterHit = new Dictionary<int, float>();
        private float _hpSyncTick;

        private static int GameHealth(PlayerAvatar p) => p != null && p.playerHealth != null ? Compat.Get(p.playerHealth, "health", 100) : 100;

        private void HostDamage(PlayerAvatar h, int dmg, string how)
        {
            if (h == null || !IsAlive(h) || dmg <= 0) return;
            int id = h.photonView.ViewID;
            if (!_hp.TryGetValue(id, out int hp)) hp = GameHealth(h);
            hp = Mathf.Max(0, hp - dmg);
            _hp[id] = hp;
            _lastMonsterHit[id] = Time.time;
            Plugin.Log.LogInfo($"[Damage] {NameOf(h)} -{dmg} by {how} -> {hp} HP");
            Send(MsgDamage, id, dmg, hp);
            if (hp <= 0) HostKill(h);
        }

        private void HostDamageDummy(TestDummy d, int dmg, Vector3 from, string how)
        {
            if (d == null || d.Dead) return;
            d.Hp = Mathf.Max(0, d.Hp - dmg);
            _lastAttackInfo = L.Damage(d.DummyName, dmg, d.Hp) + "  (" + how + ")";
            if (d.Hp <= 0) KillDummy(d, from, how);
            else Banner(L.Damage(d.DummyName, dmg, d.Hp), new Color(1f, 0.55f, 0.3f), 1.2f);
        }

        /// <summary>Host: follow the game's health (heals, fall damage ...) when the monster hasn't hit for a while.</summary>
        private void HostSyncHealth()
        {
            _hpSyncTick -= Time.deltaTime;
            if (_hpSyncTick > 0f) return;
            _hpSyncTick = 1f;
            foreach (var h in AliveHiders())
            {
                int id = h.photonView.ViewID;
                if (_lastMonsterHit.TryGetValue(id, out float t) && Time.time - t < 1.5f) continue;
                _hp[id] = GameHealth(h);
            }
        }

        private bool RecentlyHitByMonster(int id) => _lastMonsterHit.TryGetValue(id, out float t) && Time.time - t < 2.5f;

        private void OnDamage(int victimId, int dmg, int hpLeft)
        {
            var victim = FindByViewId(victimId);
            if (victim == null) return;
            if (Seeker != null && GameRefs.IsLocal(Seeker))
                Banner(L.Damage(NameOf(victim), dmg, hpLeft), new Color(1f, 0.55f, 0.3f), 1.2f);
            if (!victim.isLocal || !IsAlive(victim) || hpLeft <= 0) return; // lethal hits come as a kill message
            var health = victim.playerHealth;
            if (health == null) return;
            try
            {
                Compat.Set(health, "godMode", false);
                Compat.Set(health, "invincibleTimer", 0f);
                Compat.Call(health, "Hurt", dmg, false);
            }
            catch (System.Exception ex) { Plugin.Log.LogDebug("Hurt failed: " + ex.Message); }
        }

        private AbilityType CurrentAbility => _monster != null ? _monster.Ability : AbilityType.None;

        // ------------------------------------------------------------------
        // Trigger (local seeker)
        // ------------------------------------------------------------------
        private void TriggerSpecial()
        {
            var ability = CurrentAbility;
            if (ability == AbilityType.None) { Banner(L.NoSpecial, Color.white, 1.5f); return; }
            if (Time.time < _specialReadyAt) return;
            _specialReadyAt = Time.time + Cd(ability == AbilityType.ItemThrow && !_holdingItem ? 1f : Abilities.Cooldown(ability));
            Plugin.Log.LogInfo($"[Monster] special pressed: {ability}");

            bool gambled = false;
            if (ability == AbilityType.Gamble)
            {
                ability = Abilities.GambleTable[Random.Range(0, Abilities.GambleTable.Length)];
                gambled = true;
                Plugin.Log.LogInfo($"[Monster] gamble rolled: {ability}");
            }

            if (ability == AbilityType.Laser)
            {
                if (gambled) Banner(L.Gambled(Abilities.Name(ability)), Abilities.UnityColor(AbilityType.Gamble), 2f);
                Send(MsgLaser, LocalAimDirection());
                return;
            }

            Vector3 origin = _monster != null ? _monster.MuzzlePosition : SeekerPosition() + Vector3.up * 1.5f;
            Send(MsgSpecial, (int)ability, origin, LocalAimDirection(), gambled);
        }

        // ------------------------------------------------------------------
        // Received on every client
        // ------------------------------------------------------------------
        private void OnSpecial(AbilityType ability, Vector3 origin, Vector3 dir, bool gambled = false)
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            if (gambled) Banner(L.Gambled(Abilities.Name(ability)), Abilities.UnityColor(AbilityType.Gamble), 2f);
            string hiderMsg = L.AbilityUsedByMonster(ability);
            if (!localSeeker && !gambled && !string.IsNullOrEmpty(hiderMsg)) Banner(hiderMsg, Abilities.UnityColor(ability), 2f);

            switch (ability)
            {
                case AbilityType.Shot:
                    {
                        Vector3 end = RayToWall(origin, dir, 60f, out float wallDist);
                        Effects.Tracer(origin, end, Abilities.UnityColor(ability), 0.07f, 0.35f);
                        if (_monster != null) _monster.PlayAttack();
                        if (IsHost) HostShotHits(origin, dir, wallDist);
                        break;
                    }
                case AbilityType.Sprint:
                case AbilityType.FlyingCharge:
                    _chargeUntil = Time.time + Abilities.Duration(ability);
                    _chargeType = ability;
                    _chargeTickAt = Time.time + 0.15f;
                    if (_monster != null) _monster.PlayRoar();
                    if (localSeeker && ability == AbilityType.FlyingCharge)
                        Compat.Call(PlayerController.instance, "ForceImpulse", Vector3.up * 5f);
                    break;
                case AbilityType.Leap:
                    _leapUntil = Time.time + Abilities.Duration(ability);
                    _leapHit.Clear();
                    if (_monster != null) _monster.PlayAttack();
                    if (localSeeker) DoLeap(dir);
                    break;
                case AbilityType.Reveal:
                    OnRadarPing(localSeeker);
                    break;
                case AbilityType.Throw:
                    _projectiles.Add(new Projectile(origin, dir * 17f + Vector3.up * 3.5f, Abilities.UnityColor(ability)));
                    if (_monster != null) _monster.PlayAttack();
                    break;
                case AbilityType.Bomb:
                    _projectiles.Add(new Projectile(origin, dir * 13f + Vector3.up * 4.5f, Abilities.UnityColor(ability), AbilityType.Bomb, 0.7f));
                    if (_monster != null) _monster.PlayAttack();
                    break;
                case AbilityType.Puke:
                    _pukeUntil = Time.time + Abilities.Duration(ability);
                    _pukeTickAt = Time.time + 0.2f;
                    if (_monster != null) _monster.PlayRoar();
                    break;
                case AbilityType.Levitate:
                    {
                        Vector3 center = Seeker != null ? PlayerPos(Seeker) : origin;
                        var col = Abilities.UnityColor(ability);
                        Effects.Shockwave(center + Vector3.up * 0.1f, Abilities.LevitateRange * 2f, col, 0.8f);
                        if (_monster != null) _monster.PlayRoar();
                        float slamIn = Abilities.Duration(ability);
                        if (LocalHider(out var me) && Vector3.Distance(PlayerPos(me), center) <= Abilities.LevitateRange)
                            _levitateUntil = Time.time + slamIn;
                        if (IsHost)
                        {
                            var victims = AliveHiders().Where(h => Vector3.Distance(PlayerPos(h), center) <= Abilities.LevitateRange).ToList();
                            var dummies = AliveDummies().Where(d => Vector3.Distance(d.transform.position, center) <= Abilities.LevitateRange).ToList();
                            foreach (var d in dummies) d.transform.position += Vector3.up * 2f;
                            Later(slamIn, () =>
                            {
                                foreach (var h in victims) HostDamage(h, Abilities.LevitateDamage, "levitate");
                                foreach (var d in dummies)
                                {
                                    if (d == null) continue;
                                    d.transform.position -= Vector3.up * 2f;
                                    HostDamageDummy(d, Abilities.LevitateDamage, center, "levitate");
                                }
                            });
                        }
                        break;
                    }
                case AbilityType.Slam:
                    if (_monster != null) _monster.PlayAttack();
                    Later(Abilities.Duration(ability), () =>
                    {
                        Vector3 c = Seeker != null ? PlayerPos(Seeker) : origin;
                        Effects.Shockwave(c + Vector3.up * 0.2f, Abilities.SlamRadius * 2.2f, Abilities.UnityColor(AbilityType.Slam), 0.5f);
                        if (_monster != null) _monster.PlayRoar();
                        if (LocalHider(out var me) && FlatDistance(PlayerPos(me), c) <= Abilities.SlamRadius && Mathf.Abs(PlayerPos(me).y - c.y) < 2.5f)
                            LocalPush(Away(c, PlayerPos(me)) * 9f + Vector3.up * 5f);
                        if (IsHost)
                        {
                            foreach (var h in AliveHiders())
                                if (FlatDistance(PlayerPos(h), c) <= Abilities.SlamRadius && Mathf.Abs(PlayerPos(h).y - c.y) < 2.5f)
                                    HostDamage(h, Abilities.SlamDamage, "slam");
                            foreach (var d in AliveDummies())
                                if (FlatDistance(d.transform.position, c) <= Abilities.SlamRadius) HostDamageDummy(d, Abilities.SlamDamage, c, "slam");
                        }
                    });
                    break;
                case AbilityType.Swarm:
                    OnSwarm(origin);
                    break;
                case AbilityType.ItemThrow:
                    {
                        // throw direction: where the monster's camera looked when pressing
                        if (IsHost) HostItemThrow(dir);
                        if (_holdingItem && _monster != null) _monster.PlayAttack();
                        break;
                    }
                case AbilityType.Tornado:
                    SpawnTornado(origin);
                    break;
                case AbilityType.Launch:
                    if (IsHost) HostLaunch(dir);
                    break;
                case AbilityType.Impulse:
                    OnImpulse(origin);
                    break;
                case AbilityType.Charge:
                case AbilityType.Lunge:
                    {
                        _dashType = ability;
                        _dashStart = Time.time + Abilities.DashWindup;
                        _dashEnd = _dashStart + Abilities.DashTime(_dashType);
                        _dashHit.Clear();
                        // Charge: flat. Lunge: exactly where the camera looks.
                        Vector3 d = dir;
                        if (localSeeker) { var v = GameRefs.ViewTransform(Seeker); if (v != null) d = v.forward; }
                        if (ability == AbilityType.Charge) { d.y = 0f; if (d.sqrMagnitude < 0.001f) d = MonsterForward(); }
                        _dashDir = d.normalized;
                        if (_monster != null) _monster.PlayRoar();
                        break;
                    }
                case AbilityType.NoxiousGas:
                    {
                        Vector3 c = (Seeker != null ? PlayerPos(Seeker) : origin) + Vector3.up * 1.2f;
                        Vector3 fwd = MonsterForward();
                        var col = Abilities.UnityColor(ability);
                        for (int k = 0; k < Abilities.GasClouds; k++)
                        {
                            // one cloud stays on the monster, the others drift out in a ring
                            Vector3 d = k == 0 ? Vector3.zero : Quaternion.Euler(0, (k - 1) * 90f + 45f, 0) * fwd;
                            _gas.Add(new GasCloud(c, d * Abilities.GasSpread, Time.time + Abilities.GasTime, col));
                        }
                        if (_monster != null) _monster.PlayRoar();
                        break;
                    }
                case AbilityType.FlyingGrab:
                case AbilityType.ZeroGGrab:
                    _grabUntil = Time.time + Abilities.Duration(ability);
                    _grabType = ability;
                    _grabStunShown = false;
                    _grabCaught = false;
                    _grabFxAt = 0f;
                    if (_monster != null) _monster.PlayAttack();
                    Plugin.Log.LogInfo($"[Monster] {ability} active for {Abilities.Duration(ability)}s, radius {Abilities.GrabRadius(ability)}m");
                    break;
                case AbilityType.Pull:
                    {
                        Vector3 center = Seeker != null ? PlayerPos(Seeker) : origin;
                        _pullUntil = Time.time + Abilities.Duration(ability);
                        _pullFxAt = 0f;
                        _pulled = LocalHider(out var me) && Vector3.Distance(PlayerPos(me), center) <= Abilities.PullRange;
                        if (_monster != null) _monster.PlayRoar();
                        break;
                    }
                case AbilityType.Shockwave:
                    {
                        Vector3 center = Seeker != null ? PlayerPos(Seeker) : origin;
                        Effects.Shockwave(center + Vector3.up * 0.5f, 20f, Abilities.UnityColor(ability));
                        if (_monster != null) _monster.PlayRoar();
                        var local = GameRefs.LocalAvatar;
                        if (!localSeeker && local != null && IsAlive(local) && FlatDistance(PlayerPos(local), center) < 10f)
                        {
                            _stunUntil = Time.time + Abilities.Duration(ability);
                            Banner(L.Stunned, Abilities.UnityColor(ability), 2f);
                            LocalPush(Away(center, PlayerPos(local)) * 7f + Vector3.up * 2.5f);
                        }
                        break;
                    }
            }
        }

        private void DoLeap(Vector3 dir)
        {
            var flat = new Vector3(dir.x, 0, dir.z);
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            var pc = PlayerController.instance;
            // The game's own movement helpers (names checked at runtime, see Compat).
            if (!Compat.Call(pc, "MoveForce", flat, 22f, 0.45f)) _lungeUntil = Time.time + 0.8f;
            Compat.Call(pc, "ForceImpulse", Vector3.up * 6f + flat * 4f);
        }

        // ------------------------------------------------------------------
        // Per frame (all clients)
        // ------------------------------------------------------------------
        private void AbilityTick()
        {
            // projectiles
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                Vector3 from = p.Position;
                p.Step(Time.deltaTime);
                bool hitWall = Physics.Linecast(from, p.Position, out var hit, ~0, QueryTriggerInteraction.Ignore) && !IsPlayerCollider(hit.collider);
                if (p.Kind == AbilityType.Bomb)
                {
                    // The head is a physical object: it bounces off walls/floor and rolls out, then explodes after 4 s.
                    Vector3 delta = p.Position - from;
                    if (!p.Resting && delta.sqrMagnitude > 0.000001f
                        && Physics.SphereCast(from, 0.3f, delta.normalized, out var bh, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                        && !IsPlayerCollider(bh.collider))
                    {
                        p.Position = from + delta.normalized * Mathf.Max(0f, bh.distance - 0.02f) + bh.normal * 0.02f;
                        p.Velocity = Vector3.Reflect(p.Velocity, bh.normal) * 0.55f;
                        if (bh.normal.y > 0.6f)
                        {
                            p.Velocity = new Vector3(p.Velocity.x * 0.75f, p.Velocity.y, p.Velocity.z * 0.75f);
                            if (p.Velocity.magnitude < 0.8f) { p.Resting = true; p.Velocity = Vector3.zero; }
                        }
                        p.SyncVisual();
                    }
                    p.Blink(4f);
                    if (p.Age > 4f)
                    {
                        BombExplode(p.Position);
                        p.Destroy();
                        _projectiles.RemoveAt(i);
                    }
                    continue;
                }
                if (IsHost && Phase == Phase.Hunting) HostProjectileHits(p);
                if (hitWall || p.Dead || p.Age > 5f)
                {
                    if (hitWall) Effects.Shockwave(hit.point, 1.5f, p.Color, 0.25f);
                    p.Destroy();
                    _projectiles.RemoveAt(i);
                }
            }

            if (IsHost && Phase == Phase.Hunting && Seeker != null)
            {
                HostSyncHealth();
                Vector3 sp = SeekerPosition();

                // leap: each hider can be hit once per leap
                if (Time.time < _leapUntil)
                {
                    foreach (var h in AliveHiders())
                        if (FlatDistance(sp, PlayerPos(h)) <= 2.4f && Mathf.Abs(PlayerPos(h).y - sp.y) < 2.5f && _leapHit.Add(h.photonView.ViewID))
                            HostDamage(h, Abilities.LeapDamage, "leap");
                    foreach (var d in AliveDummies())
                        if (FlatDistance(sp, d.transform.position) <= 2.4f && _leapHit.Add(-1000 - d.GetInstanceID()))
                            HostDamageDummy(d, Abilities.LeapDamage, sp, "leap");
                }

                // Rapid / Flying Charge: damage every 0.5 s to everyone in the radius
                if (Charging && Time.time >= _chargeTickAt)
                {
                    _chargeTickAt = Time.time + Abilities.DamageTick;
                    int dmg = Abilities.ChargeDamage(_chargeType);
                    string how = Abilities.Name(_chargeType);
                    foreach (var h in AliveHiders())
                        if (InChargeRadius(sp, PlayerPos(h))) HostDamage(h, dmg, how);
                    foreach (var d in AliveDummies())
                        if (InChargeRadius(sp, d.transform.position)) HostDamageDummy(d, dmg, sp, how);
                }
            }

            // local seeker: flying during Flying Charge (jump = up, crouch = down)
            if (Charging && _chargeType == AbilityType.FlyingCharge && Seeker != null && GameRefs.IsLocal(Seeker))
                Safe("fly", FlyTick);

            // local hider: stunned by a shockwave
            if (Time.time < _stunUntil)
                Compat.Call(PlayerController.instance, "OverrideSpeed", 0.25f, 0.2f);

            RunLater();
            Safe("grab zone", GrabTick);
            Safe("gas", GasTick);
            Safe("minions", MinionTick);
            Safe("dash", DashTick);
            Safe("item", ItemTick);
            Safe("tornado", TornadoTick);
            Safe("launch", LaunchTick);
            Safe("impulse", ImpulseTick);
            if (Seeker != null && GameRefs.IsLocal(Seeker) && Phase == Phase.Hunting && Abilities.FreeFlight(_monsterName)
                && !(Charging && _chargeType == AbilityType.FlyingCharge))
                Safe("free flight", FlyTick);
            Safe("clouds", CloudTick);
            Safe("puke", PukeTick);
            Safe("pull", PullTick);
            Safe("levitate", LevitateTick);
            Safe("charge fx", ChargeFxTick);
            if (_monster != null) _monster.SetAttackForm(Charging && _chargeType == AbilityType.FlyingCharge && CurrentAbility == AbilityType.FlyingCharge); // duck only, not a Gambit roll
        }

        // ------------------------------------------------------------------
        // Helpers for the game-like specials
        // ------------------------------------------------------------------
        private float _pukeUntil, _pukeTickAt, _pukeFxAt, _pullUntil, _pullFxAt, _levitateUntil, _chargeFxAt;
        private bool _pulled, _levitating;
        private readonly List<(Vector3 pos, float until)> _clouds = new List<(Vector3, float)>();
        private float _cloudTickAt;
        private readonly List<(float at, System.Action act)> _later = new List<(float, System.Action)>();

        private void Later(float delay, System.Action act) => _later.Add((Time.time + delay, act));

        private void RunLater()
        {
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (Time.time < _later[i].at) continue;
                var act = _later[i].act;
                _later.RemoveAt(i);
                Safe("later", act);
            }
        }

        /// <summary>The local player, if they are an alive hider of this round.</summary>
        private bool LocalHider(out PlayerAvatar me)
        {
            me = GameRefs.LocalAvatar;
            return me != null && IsAlive(me) && Seeker != null && !GameRefs.IsLocal(Seeker) && _hiderViewIds.Contains(me.photonView.ViewID);
        }

        private static Vector3 Away(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0;
            return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
        }

        /// <summary>Push the local player (game's own impulse, physics fallback).</summary>
        private static void LocalPush(Vector3 impulse)
        {
            var pc = PlayerController.instance;
            if (pc == null) return;
            if (!Compat.Call(pc, "ForceImpulse", impulse))
            {
                var rb = Compat.Get<Rigidbody>(pc, "rb");
                if (rb != null) rb.AddForce(impulse, ForceMode.VelocityChange);
            }
        }

        private Vector3 MonsterForward()
        {
            if (Seeker != null && GameRefs.IsLocal(Seeker))
            {
                var v = GameRefs.ViewTransform(Seeker);
                if (v != null) { var f = v.forward; f.y = 0; if (f.sqrMagnitude > 0.001f) return f.normalized; }
            }
            if (_monster != null) return _monster.transform.forward;
            return Seeker != null ? Seeker.transform.forward : Vector3.forward;
        }

        private void BombExplode(Vector3 pos)
        {
            var col = Abilities.UnityColor(AbilityType.Bomb);
            Effects.Shockwave(pos, Abilities.BombRadius * 2.2f, new Color(1f, 0.85f, 0.4f), 0.45f);
            Effects.Shockwave(pos, Abilities.CloudRadius * 2f, new Color(col.r, col.g, col.b, 0.5f), Abilities.CloudTime);
            _clouds.Add((pos, Time.time + Abilities.CloudTime));
            if (_monster != null) _monster.PlayRoar();
            if (LocalHider(out var me) && Vector3.Distance(PlayerPos(me) + Vector3.up * 0.9f, pos) <= Abilities.BombRadius)
            {
                LocalPush(Away(pos, PlayerPos(me)) * 8f + Vector3.up * 4f);
                _flashUntil = Time.time + 0.4f;
            }
            if (!IsHost || Phase != Phase.Hunting) return;
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, pos) <= Abilities.BombRadius) HostDamage(h, Abilities.BombDamage, "bomb");
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, pos) <= Abilities.BombRadius) HostDamageDummy(d, Abilities.BombDamage, pos, "bomb");
        }

        private void CloudTick()
        {
            _clouds.RemoveAll(c => Time.time >= c.until);
            if (_clouds.Count == 0 || !IsHost || Phase != Phase.Hunting || Time.time < _cloudTickAt) return;
            _cloudTickAt = Time.time + Abilities.DamageTick;
            foreach (var h in AliveHiders())
                if (_clouds.Any(c => Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, c.pos) <= Abilities.CloudRadius))
                    HostDamage(h, Abilities.CloudDamage, "uranium cloud");
            foreach (var d in AliveDummies())
                if (_clouds.Any(c => Vector3.Distance(d.transform.position + Vector3.up * 0.9f, c.pos) <= Abilities.CloudRadius))
                    HostDamageDummy(d, Abilities.CloudDamage, d.transform.position, "uranium cloud");
        }

        private bool InPukeCone(Vector3 mouth, Vector3 fwd, Vector3 target)
        {
            Vector3 to = target - mouth;
            Vector3 flat = new Vector3(to.x, 0, to.z);
            if (flat.magnitude > Abilities.PukeRange || Mathf.Abs(to.y) > 3f) return false;
            return flat.magnitude < 1f || Vector3.Angle(flat, fwd) < 35f;
        }

        private void PukeTick()
        {
            if (Time.time >= _pukeUntil || Seeker == null) return;
            Vector3 fwd = MonsterForward();
            Vector3 mouth = (_monster != null ? _monster.transform.position : PlayerPos(Seeker)) + Vector3.up * Mathf.Clamp((_monster != null ? _monster.BodyHeight : 2f) * 0.6f, 0.8f, 3f) + fwd * 0.6f;
            var col = Abilities.UnityColor(AbilityType.Puke);
            if (Time.time >= _pukeFxAt)
            {
                _pukeFxAt = Time.time + 0.04f;
                Vector3 v = (fwd + Random.insideUnitSphere * 0.25f + Vector3.up * 0.15f).normalized * Random.Range(8f, 11f);
                Effects.Blob(mouth, v, col, 0.9f);
            }
            if (LocalHider(out var me) && InPukeCone(mouth, fwd, PlayerPos(me) + Vector3.up * 0.9f))
                Compat.Call(PlayerController.instance, "OverrideSpeed", 0.5f, 0.2f);
            if (!IsHost || Phase != Phase.Hunting || Time.time < _pukeTickAt) return;
            _pukeTickAt = Time.time + Abilities.DamageTick;
            foreach (var h in AliveHiders())
                if (InPukeCone(mouth, fwd, PlayerPos(h) + Vector3.up * 0.9f)) HostDamage(h, Abilities.PukeDamage, "puke");
            foreach (var d in AliveDummies())
                if (InPukeCone(mouth, fwd, d.transform.position + Vector3.up * 0.9f)) HostDamageDummy(d, Abilities.PukeDamage, mouth, "puke");
        }

        private void PullTick()
        {
            if (Time.time >= _pullUntil || Seeker == null) return;
            Vector3 center = PlayerPos(Seeker);
            if (Time.time >= _pullFxAt)
            {
                _pullFxAt = Time.time + 0.5f;
                Effects.Implode(center + Vector3.up * 0.1f, Abilities.PullRange * 2f, Abilities.UnityColor(AbilityType.Pull), 0.5f);
            }
            if (!_pulled || !LocalHider(out var me)) return;
            var pc = PlayerController.instance;
            Compat.Call(pc, "OverrideSpeed", 0.6f, 0.2f);
            var rb = Compat.Get<Rigidbody>(pc, "rb");
            if (rb == null) return;
            Vector3 to = center - PlayerPos(me); to.y = 0;
            if (to.magnitude < 1.5f) return;
            rb.velocity += to.normalized * 18f * Time.deltaTime;
        }

        private void LevitateTick()
        {
            if (Time.time < _levitateUntil)
            {
                _levitating = true;
                var pc = PlayerController.instance;
                Compat.Call(pc, "AntiGravity", 0.25f);
                var rb = Compat.Get<Rigidbody>(pc, "rb");
                if (rb != null) { var v = rb.velocity; v.y = Mathf.MoveTowards(v.y, 3.5f, 25f * Time.deltaTime); rb.velocity = v; }
            }
            else if (_levitating)
            {
                _levitating = false;
                LocalPush(Vector3.down * 18f);
                _flashUntil = Time.time + 0.3f;
            }
        }

        /// <summary>Rapid / Flying Charge: coloured aura pulses around the monster (everyone sees it).</summary>
        private void ChargeFxTick()
        {
            if (!Charging || Seeker == null || Time.time < _chargeFxAt) return;
            _chargeFxAt = Time.time + 0.25f;
            bool ownFirstPerson = GameRefs.IsLocal(Seeker) && !ThirdPersonCamera.Working;
            Vector3 c = (_monster != null ? _monster.transform.position : PlayerPos(Seeker));
            float h = _monster != null ? _monster.BodyHeight : 2f;
            var col = Abilities.UnityColor(_chargeType);
            float r = Abilities.ChargeRadius * Mathf.Max(1f, _roundScale);
            Effects.Ring(c + Vector3.up * 0.05f, r * 2f, new Color(col.r, col.g, col.b, 0.3f), 0.4f);
            if (ownFirstPerson) return; // sparks right in front of the camera would only flash
            Vector3 back = -MonsterForward();
            for (int i = 0; i < 2; i++)
                Effects.Blob(c + Vector3.up * Random.Range(0.2f, h) + Random.insideUnitSphere * 0.5f, back * 4f + Vector3.up, col, 0.4f, false);
        }

        private void DashTick()
        {
            if (Seeker == null || Time.time >= _dashEnd + 0.1f) return;
            bool localSeeker = GameRefs.IsLocal(Seeker);
            var pc = PlayerController.instance;
            if (localSeeker && pc != null)
            {
                var rb = Compat.Get<Rigidbody>(pc, "rb");
                if (DashWindup)
                {
                    // frozen while winding up
                    Compat.Call(pc, "OverrideSpeed", 0f, 0.2f);
                    if (rb != null) rb.velocity = new Vector3(0f, _dashType == AbilityType.Lunge ? 0f : rb.velocity.y, 0f);
                    if (_dashType == AbilityType.Lunge) Compat.Call(pc, "AntiGravity", 0.2f);
                }
                else if (Dashing && rb != null)
                {
                    Vector3 v = _dashDir * Abilities.DashSpeed;
                    if (_dashType == AbilityType.Charge) v.y = rb.velocity.y; // stays on the ground
                    else Compat.Call(pc, "AntiGravity", 0.2f);
                    rb.velocity = v;
                }
                else if (rb != null && Time.time < _dashEnd + 0.1f)
                {
                    // stop after the dash instead of sliding on
                    var v = rb.velocity; rb.velocity = new Vector3(v.x * 0.2f, Mathf.Min(v.y, 2f), v.z * 0.2f);
                }
            }

            // host: 60 damage once per target while dashing
            if (!IsHost || Phase != Phase.Hunting || !Dashing) return;
            Vector3 sp = SeekerPosition();
            float r = Abilities.DashHitRadius * Mathf.Max(1f, _roundScale);
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.8f, sp + Vector3.up * 0.8f) <= r && _dashHit.Add(h.photonView.ViewID))
                    HostDamage(h, Abilities.DashDamage, Abilities.Name(_dashType));
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position, sp) <= r + 0.4f && _dashHit.Add(-1000 - d.GetInstanceID()))
                    HostDamageDummy(d, Abilities.DashDamage, sp, Abilities.Name(_dashType));
        }

        private bool InChargeRadius(Vector3 seekerPos, Vector3 target)
        {
            float dy = Mathf.Abs(target.y - seekerPos.y);
            float r = Abilities.ChargeRadius * Mathf.Max(1f, _roundScale);
            return FlatDistance(seekerPos, target) <= r && dy < (_chargeType == AbilityType.FlyingCharge ? 3f : 2.5f);
        }

        /// <summary>Flying Grab / Zero-G Grab: hiders inside the bubble around the monster are stunned (and float).</summary>
        // ---- Noxious Gas (all clients) ----
        private readonly List<GasCloud> _gas = new List<GasCloud>();
        private bool _tranqShown;
        private float _tranqUntil;

        private void GasTick()
        {
            for (int i = _gas.Count - 1; i >= 0; i--)
            {
                if (!_gas[i].Step(Time.deltaTime)) { _gas[i].Destroy(); _gas.RemoveAt(i); }
            }
            if (_gas.Count == 0) { _tranqShown = false; return; }

            foreach (var d in AliveDummies())
                if (_gas.Any(g => g.Contains(d.transform.position + Vector3.up * 0.9f))) d.Stun(0.3f, false);

            if (!LocalHider(out var me)) return;
            if (!_gas.Any(g => g.Contains(PlayerPos(me) + Vector3.up * 0.9f))) return;
            _tranqUntil = Time.time + 0.3f;
            var pc = PlayerController.instance;
            Compat.Call(pc, "OverrideSpeed", 0.3f, 0.2f);
            Compat.Call(pc, "OverrideLookSpeed", 0.25f, 0.5f, 1f, 0.2f);
            if (!_tranqShown) { _tranqShown = true; Banner(L.Tranquilized, Abilities.UnityColor(AbilityType.NoxiousGas), 2.5f); }
        }

        private void DrawTranqOverlay(float sw, float sh)
        {
            if (Time.time >= _tranqUntil) return;
            var c = Abilities.UnityColor(AbilityType.NoxiousGas);
            GUI.color = new Color(c.r, c.g, c.b, 0.16f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
            GUI.color = Color.white;
        }

        private float _grabFxAt;

        private void GrabTick()
        {
            if (!Grabbing || Seeker == null) return;
            Vector3 c = PlayerPos(Seeker);
            float r = Abilities.GrabRadius(_grabType) * Mathf.Max(1f, _roundScale);
            bool zeroG = _grabType == AbilityType.ZeroGGrab;

            // test dummies (host only)
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position, c) <= r) d.Stun(0.3f, zeroG);

            // the monster: faint ring showing the reach (hiders don't see it – it's an invisible bubble) + count
            if (GameRefs.IsLocal(Seeker))
            {
                if (Time.time >= _grabFxAt)
                {
                    _grabFxAt = Time.time + 0.5f;
                    var col = Abilities.UnityColor(_grabType);
                    Effects.Ring(c + Vector3.up * 0.05f, r * 2f, new Color(col.r, col.g, col.b, 0.25f), 0.5f);
                    int n = AliveHiders().Count(h => Vector3.Distance(PlayerPos(h), c) <= r)
                            + AliveDummies().Count(d => Vector3.Distance(d.transform.position, c) <= r);
                    Banner(L.GrabCaught(Abilities.Name(_grabType), n), col, 0.6f);
                }
                return;
            }

            if (!LocalHider(out var me)) return;
            // Once caught you stay caught until the ability ends – even if the monster carries you out of the bubble.
            if (!_grabCaught && Vector3.Distance(PlayerPos(me), c) > r) return;
            var pc = PlayerController.instance;
            _stunUntil = Mathf.Max(_stunUntil, Time.time + 0.3f); // stun overlay + slow (AbilityTick)
            Compat.Call(pc, "OverrideSpeed", 0.1f, 0.2f);

            // Knock the hider into the game's tumble (ragdoll): only tumbling players can be grabbed and carried.
            var tumble = Compat.Get<Component>(me, "tumble");
            if (tumble != null)
            {
                if (!_grabCaught)
                {
                    Compat.Call(tumble, "TumbleRequest", true, false);
                    Plugin.Log.LogInfo($"[Monster] caught by {_grabType}: tumbling");
                    if (_grabType == AbilityType.ZeroGGrab) Compat.Call(tumble, "TumbleForce", Vector3.up * 4f);
                }
                Compat.Call(tumble, "TumbleOverrideTime", 0.4f); // can't get up while caught
                if (_grabType == AbilityType.ZeroGGrab) Compat.Call(tumble, "DisableCustomGravity", 0.3f);
            }
            _grabCaught = true;

            if (_grabType == AbilityType.ZeroGGrab && tumble == null)
            {
                Compat.Call(pc, "AntiGravity", 0.25f);
                var rb = Compat.Get<Rigidbody>(pc, "rb");
                if (rb != null) { var v = rb.velocity; v.y = Mathf.MoveTowards(v.y, 1.2f, 10f * Time.deltaTime); rb.velocity = v; }
                else LocalPush(Vector3.up * 0.3f);
            }
            if (!_grabStunShown)
            {
                _grabStunShown = true;
                Banner(L.Stunned, Abilities.UnityColor(_grabType), 2f);
            }
        }

        private static bool GameKey(InputKey key)
        {
            try { return SemiFunc.InputHold(key); }
            catch
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;
                return key == InputKey.Jump ? kb.spaceKey.isPressed : key == InputKey.Crouch && kb.leftCtrlKey.isPressed;
            }
        }

        private void FlyTick()
        {
            var pc = PlayerController.instance;
            if (pc == null) return;
            Compat.Call(pc, "AntiGravity", 0.25f);
            // The player's own jump / crouch keys from the game settings (rebinding works).
            float up = 0f;
            if (GameKey(InputKey.Jump)) up += 1f;
            if (GameKey(InputKey.Crouch)) up -= 1f;
            // Don't fly into the ceiling (the camera would end up inside it).
            var head = GameRefs.MainCamera != null ? GameRefs.MainCamera.transform.position : pc.transform.position + Vector3.up * 1.6f;
            if (up > 0f && Physics.SphereCast(head, 0.3f, Vector3.up, out var ceil, 1.2f, ~0, QueryTriggerInteraction.Ignore)
                && !IsPlayerCollider(ceil.collider))
                up = ceil.distance < 0.6f ? -0.3f : 0f;
            var rb = Compat.Get<Rigidbody>(pc, "rb");
            if (rb == null) return;
            var v = rb.velocity;
            v.y = Mathf.MoveTowards(v.y, up * 6f, 30f * Time.deltaTime);
            rb.velocity = v;
        }

        // =====================================================================
        // Radar ping (X-ray): snapshot of all hiders within range; every hider is told whether they were seen
        // =====================================================================
        private void OnRadarPing(bool localSeeker)
        {
            Vector3 center = Seeker != null ? PlayerPos(Seeker) : Vector3.zero;
            var col = Abilities.UnityColor(AbilityType.Reveal);
            Effects.Shockwave(center + Vector3.up * 0.1f, Abilities.RadarRange * 2f, col, 1.2f);
            if (_monster != null) _monster.PlayRoar();

            if (localSeeker)
            {
                _radarTargets.Clear();
                foreach (var h in AliveHiders())
                    if (Vector3.Distance(PlayerPos(h), center) <= Abilities.RadarRange) _radarTargets.Add((PlayerPos(h) + Vector3.up * 1.9f, NameOf(h)));
                foreach (var d in AliveDummies())
                    if (Vector3.Distance(d.transform.position, center) <= Abilities.RadarRange) _radarTargets.Add((d.transform.position + Vector3.up * 2f, d.DummyName));
                _revealUntil = Time.time + Abilities.Duration(AbilityType.Reveal);
                Banner(L.RadarFound(_radarTargets.Count), col, 2f);
                return;
            }

            var local = GameRefs.LocalAvatar;
            if (local == null || !IsAlive(local) || !_hiderViewIds.Contains(local.photonView.ViewID)) return;
            bool detected = Vector3.Distance(PlayerPos(local), center) <= Abilities.RadarRange;
            if (detected)
            {
                _flashUntil = Time.time + 0.8f;
                Banner(L.RadarDetected, new Color(1f, 0.25f, 0.25f), 3f);
            }
            else Banner(L.RadarSafe, new Color(0.45f, 1f, 0.55f), 3f);
        }

        private void ClearAbilities()
        {
            foreach (var p in _projectiles) p.Destroy();
            _projectiles.Clear();
            _revealUntil = _leapUntil = _stunUntil = _chargeUntil = 0f;
            _radarTargets.Clear();
            _leapHit.Clear();
            _hp.Clear();
            _lastMonsterHit.Clear();
            _later.Clear();
            _clouds.Clear();
            ClearMinions();
            ClearItem();
            ClearTornados();
            ClearTumbleSpecials();
            foreach (var g in _gas) g.Destroy();
            _gas.Clear();
            _tranqUntil = 0f;
            _pukeUntil = _pullUntil = _levitateUntil = _grabUntil = _dashStart = _dashEnd = 0f;
            _pulled = _levitating = false;
            if (_monster != null) _monster.SetAttackForm(false);
        }

        // ------------------------------------------------------------------
        // Host hit tests
        // ------------------------------------------------------------------
        private Vector3 RayToWall(Vector3 origin, Vector3 dir, float range, out float dist)
        {
            dist = range;
            foreach (var h in Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore))
                if (!IsPlayerCollider(h.collider) && h.distance < dist) dist = h.distance;
            return origin + dir * dist;
        }

        private void HostShotHits(Vector3 origin, Vector3 dir, float maxDist)
        {
            PlayerAvatar bestH = null; TestDummy bestD = null; float best = maxDist;
            foreach (var h in AliveHiders())
            {
                Vector3 c = PlayerPos(h) + Vector3.up * 0.9f;
                float along = Vector3.Dot(c - origin, dir);
                if (along < 0 || along > best) continue;
                if (Vector3.Distance(origin + dir * along, c) < 0.7f) { best = along; bestH = h; bestD = null; }
            }
            foreach (var d in AliveDummies())
            {
                Vector3 c = d.transform.position + Vector3.up * 0.9f;
                float along = Vector3.Dot(c - origin, dir);
                if (along < 0 || along > best) continue;
                if (Vector3.Distance(origin + dir * along, c) < 0.7f) { best = along; bestD = d; bestH = null; }
            }
            if (bestH != null) HostDamage(bestH, Abilities.ShotDamage, "shot");
            if (bestD != null) HostDamageDummy(bestD, Abilities.ShotDamage, origin, "shot");
        }

        private void HostProjectileHits(Projectile p)
        {
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, p.Position) < 1.1f) { HostDamage(h, Abilities.ThrowDamage, "throw"); p.Dead = true; return; }
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, p.Position) < 1.1f)
                {
                    HostDamageDummy(d, Abilities.ThrowDamage, p.Position, "throw");
                    p.Dead = true;
                    return;
                }
        }

        // ------------------------------------------------------------------
        // X-ray markers (local seeker)
        // ------------------------------------------------------------------
        private void DrawRevealMarkers(float u)
        {
            if (Time.time >= _revealUntil) return;
            var cam = GameRefs.MainCamera;
            if (cam == null) return;

            var targets = _radarTargets;
            if (targets.Count == 0) return;

            // Project like the rendered (possibly third-person) camera.
            Vector3 saved = cam.transform.position;
            if (ThirdPersonCamera.Working) cam.transform.position += ThirdPersonCamera.LastOffset;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(16 * u) };
            var col = Abilities.UnityColor(AbilityType.Reveal);
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 8f);
            foreach (var t in targets)
            {
                Vector3 sp = cam.WorldToScreenPoint(t.pos);
                if (sp.z <= 0) continue;
                float x = sp.x * Screen.width / cam.pixelWidth, y = Screen.height - sp.y * Screen.height / cam.pixelHeight;
                float s = 18 * u;
                GUI.color = new Color(col.r, col.g, col.b, pulse);
                GUI.DrawTexture(new Rect(x - s / 2, y - s / 2, s, s), _white);
                GUI.color = Color.white;
                float dist = Vector3.Distance(saved, t.pos);
                GUI.Label(new Rect(x - 120 * u, y + s * 0.6f, 240 * u, 40 * u), $"{t.name}  {dist:0}m", style);
            }
            cam.transform.position = saved;
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Ability bar (local seeker HUD)
        // ------------------------------------------------------------------
        private void DrawAbilityBar(float sw, float sh, float u)
        {
            var ability = CurrentAbility;
            var slots = new List<(string key, string name, float readyAt, float total, Color color)>
            {
                (BindName(_attackAction), L.Attack, AttackBlocked ? Mathf.Max(_attackReadyAt, AttackBlockedUntil) : _attackReadyAt,
                    AttackBlocked ? (Charging ? Abilities.Duration(_chargeType) : Grabbing ? Abilities.Duration(_grabType) : Abilities.DashWindup + Abilities.DashTime(_dashType)) : Plugin.AttackCooldown.Value, new Color(1f, 0.35f, 0.3f)),
                (BindName(_roarAction), L.Roar, _roarReadyAt, Plugin.RoarCooldown.Value, new Color(1f, 0.6f, 0.3f)),
            };
            if (ability != AbilityType.None)
                slots.Add((BindName(_specialAction), "★ " + Abilities.Name(ability), _specialReadyAt, Abilities.Cooldown(ability), Abilities.UnityColor(ability)));
            slots.Add((BindName(_viewAction), L.Camera, 0f, 1f, new Color(0.7f, 0.7f, 0.75f)));

            float w = 170 * u, h = 64 * u, gap = 10 * u;
            float total = slots.Count * w + (slots.Count - 1) * gap;
            float x = (sw - total) / 2f, y = sh - h - 18 * u;
            var keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(13 * u), fontStyle = FontStyle.Bold };
            var nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(18 * u), fontStyle = FontStyle.Bold, wordWrap = true };
            var cdStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = Mathf.RoundToInt(13 * u) };

            foreach (var s in slots)
            {
                var r = new Rect(x, y, w, h);
                float left = s.readyAt - Time.time;
                bool ready = left <= 0f;
                GUI.color = new Color(0.05f, 0.04f, 0.045f, 0.85f);
                GUI.DrawTexture(r, _white);
                // colour strip + cooldown fill
                GUI.color = ready ? s.color : new Color(s.color.r, s.color.g, s.color.b, 0.35f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 5 * u, r.width * (ready ? 1f : 1f - Mathf.Clamp01(left / Mathf.Max(0.01f, s.total))), 5 * u), _white);
                GUI.color = ready ? new Color(s.color.r, s.color.g, s.color.b, 0.9f) : new Color(1, 1, 1, 0.4f);
                GUI.Label(new Rect(r.x + 8 * u, r.y + 4 * u, r.width, 20 * u), s.key, keyStyle);
                GUI.color = ready ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.6f);
                GUI.Label(new Rect(r.x + 4 * u, r.y + 10 * u, r.width - 8 * u, r.height - 14 * u), s.name, nameStyle);
                GUI.color = Color.white;
                if (!ready) GUI.Label(new Rect(r.x, r.y, r.width - 8 * u, r.height - 8 * u), $"{left:0.0}s", cdStyle);
                x += w + gap;
            }
            GUI.color = Color.white;
        }

        private void DrawStunOverlay(float sw, float sh)
        {
            if (Time.time >= _stunUntil) return;
            var c = Abilities.UnityColor(AbilityType.Shockwave);
            GUI.color = new Color(c.r, c.g, c.b, 0.10f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
            GUI.color = Color.white;
        }
    }

    /// <summary>A big drifting gas cloud (Heart Hugger). Simulated on every client.</summary>
    internal class GasCloud
    {
        private Vector3 _pos, _vel;
        private readonly float _until, _born;
        private readonly GameObject _go;
        private readonly Renderer _r;
        private readonly Color _c;

        public GasCloud(Vector3 pos, Vector3 vel, float until, Color color)
        {
            _pos = pos; _vel = vel; _until = until; _born = Time.time; _c = color;
            _go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = _go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            _go.transform.position = pos;
            _go.transform.localScale = Vector3.one * 0.5f;
            _r = _go.GetComponent<Renderer>();
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (_r != null)
            {
                if (sh != null) _r.material = new Material(sh);
                _r.material.color = new Color(color.r, color.g, color.b, 0f);
                _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public bool Contains(Vector3 p) => (p - _pos).sqrMagnitude <= Radius * Radius;

        private float Radius => Abilities.GasRadius * Mathf.Clamp01((Time.time - _born) / 0.8f);

        /// <summary>Returns false when the cloud is gone.</summary>
        public bool Step(float dt)
        {
            if (Time.time >= _until) return false;
            // drift out, slow down, don't go through walls
            Vector3 step = _vel * dt;
            if (step.sqrMagnitude > 0f && Physics.SphereCast(_pos, 0.5f, step.normalized, out _, step.magnitude + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                _vel = Vector3.zero;
            else _pos += step;
            _vel = Vector3.MoveTowards(_vel, Vector3.zero, _vel.magnitude * 1.5f * dt + 0.01f);
            if (_go == null) return true;
            float life = _until - Time.time;
            float a = 0.22f * Mathf.Clamp01((Time.time - _born) / 0.5f) * Mathf.Clamp01(life / 1.5f);
            float wobble = 1f + 0.05f * Mathf.Sin(Time.time * 2f + _born);
            _go.transform.position = _pos;
            _go.transform.localScale = Vector3.one * Radius * 2f * wobble;
            if (_r != null)
            {
                _r.material.color = new Color(_c.r, _c.g, _c.b, a);
                _r.enabled = !Effects.NearCamera(_pos, Radius + 0.4f); // inside: overlay instead of a full-screen sphere
            }
            return true;
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }

    /// <summary>A thrown projectile (simulated on every client, hits decided by the host).</summary>
    internal class Projectile
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public bool Dead;
        public Color Color;
        public AbilityType Kind;
        private readonly GameObject _visual;

        public Projectile(Vector3 pos, Vector3 vel, Color color, AbilityType kind = AbilityType.Throw, float size = 0.45f)
        {
            Position = pos;
            Velocity = vel;
            Color = color;
            Kind = kind;
            _visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = _visual.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            _visual.transform.localScale = Vector3.one * size;
            _visual.transform.position = pos;
            var r = _visual.GetComponent<Renderer>();
            if (r != null) r.material.color = color;
            var light = _visual.AddComponent<Light>();
            light.color = color;
            light.range = 3f;
            light.intensity = 2f;
        }

        public bool Resting;

        public void SyncVisual()
        {
            if (_visual != null) _visual.transform.position = Position;
        }

        /// <summary>Bomb: the light blinks faster and faster until it explodes.</summary>
        public void Blink(float fuse)
        {
            if (_visual == null) return;
            var l = _visual.GetComponent<Light>();
            if (l == null) return;
            float k = Mathf.Clamp01(Age / fuse);
            l.intensity = Mathf.Sin(Age * Mathf.Lerp(6f, 40f, k)) > 0f ? 3f : 0.4f;
        }

        public void Step(float dt)
        {
            Age += dt;
            if (Resting) return;
            Velocity += Physics.gravity * dt;
            Position += Velocity * dt;
            if (_visual != null)
            {
                _visual.transform.position = Position;
                _visual.transform.Rotate(400f * dt, 300f * dt, 0f);
            }
        }

        public void Destroy()
        {
            if (_visual != null) Object.Destroy(_visual);
        }
    }

    /// <summary>Small one-shot visual effects.</summary>
    internal static class Effects
    {
        private static Shader _shader;
        private static Shader Shader => _shader != null ? _shader : (_shader = UnityEngine.Shader.Find("Sprites/Default") ?? UnityEngine.Shader.Find("Unlit/Color"));

        public static void Tracer(Vector3 a, Vector3 b, Color color, float width, float life)
        {
            var go = new GameObject("HNS_Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            if (Shader != null) lr.material = new Material(Shader);
            lr.startColor = Color.white;
            lr.endColor = color;
            go.AddComponent<Fader>().Init(life, lr, null);
        }

        public static void Shockwave(Vector3 center, float maxSize, Color color, float life = 0.6f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.position = center;
            go.transform.localScale = Vector3.one * 0.5f;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (Shader != null) r.material = new Material(Shader);
                r.material.color = new Color(color.r, color.g, color.b, Mathf.Min(color.a, 0.22f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var f = go.AddComponent<Fader>();
            f.Init(life, null, r, maxSize);
            f.Flat = maxSize >= 12f; // huge waves as a ring on the floor – a sphere around the camera would tint the whole screen
        }

        /// <summary>A flat ring growing on the floor.</summary>
        public static void Ring(Vector3 center, float maxSize, Color color, float life)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.position = center;
            go.transform.localScale = new Vector3(0.5f, 0.05f, 0.5f);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (Shader != null) r.material = new Material(Shader);
                r.material.color = color;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var f = go.AddComponent<Fader>();
            f.Init(life, null, r, maxSize);
            f.Flat = true;
        }

        /// <summary>True when the camera is within the given distance of a point (effects there would fill the screen).</summary>
        internal static bool NearCamera(Vector3 p, float dist)
        {
            var cam = GameRefs.MainCamera;
            if (cam == null) return false;
            Vector3 cp = cam.transform.position + (ThirdPersonCamera.Working ? ThirdPersonCamera.LastOffset : Vector3.zero);
            return (cp - p).sqrMagnitude < dist * dist;
        }

        /// <summary>Small flying blob with gravity (puke, charge sparks).</summary>
        public static void Blob(Vector3 pos, Vector3 vel, Color color, float life, bool gravity = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.25f);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (Shader != null) r.material = new Material(Shader);
                r.material.color = color;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.AddComponent<Mover>().Init(vel, life, gravity, r);
        }

        /// <summary>A sphere shrinking towards its centre (pull / gas).</summary>
        public static void Implode(Vector3 center, float startSize, Color color, float life)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.position = center;
            go.transform.localScale = new Vector3(startSize, 0.08f, startSize);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (Shader != null) r.material = new Material(Shader);
                r.material.color = new Color(color.r, color.g, color.b, 0.25f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var f = go.AddComponent<Fader>();
            f.Init(life, null, r, -startSize);
            f.Flat = true;
        }

        private class Mover : MonoBehaviour
        {
            private Vector3 _v;
            private float _life, _t;
            private bool _gravity;
            private Renderer _r;
            private Color _c;

            public void Init(Vector3 v, float life, bool gravity, Renderer r)
            {
                _v = v; _life = life; _gravity = gravity; _r = r;
                if (_r != null) _c = _r.material.color;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                if (_gravity) _v += Physics.gravity * Time.deltaTime;
                transform.position += _v * Time.deltaTime;
                if (_r != null) _r.enabled = !NearCamera(transform.position, 1.2f);
                if (_r != null) _r.material.color = new Color(_c.r, _c.g, _c.b, _c.a * (1f - _t / _life));
                if (_t >= _life) Destroy(gameObject);
            }
        }

        private class Fader : MonoBehaviour
        {
            private float _life, _t, _maxSize;
            private LineRenderer _lr;
            private Renderer _r;
            private Color _c0, _c1;
            public bool Flat;

            public void Init(float life, LineRenderer lr, Renderer r, float maxSize = 0f)
            {
                _life = life; _lr = lr; _r = r; _maxSize = maxSize;
                if (_lr != null) { _c0 = _lr.startColor; _c1 = _lr.endColor; }
                if (_r != null) _c0 = _r.material.color;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / _life);
                if (_lr != null)
                {
                    _lr.startColor = new Color(_c0.r, _c0.g, _c0.b, 1f - k);
                    _lr.endColor = new Color(_c1.r, _c1.g, _c1.b, 1f - k);
                }
                if (_r != null)
                {
                    if (_maxSize > 0f)
                    {
                        float sz = Mathf.Lerp(0.5f, _maxSize, Mathf.Sqrt(k));
                        transform.localScale = Flat ? new Vector3(sz, 0.08f, sz) : Vector3.one * sz;
                        // Camera inside the sphere: the whole screen would be tinted – don't draw it.
                        if (!Flat) _r.enabled = !NearCamera(transform.position, sz * 0.5f + 0.5f);
                    }
                    else if (_maxSize < 0f)
                    {
                        float sz = Mathf.Lerp(-_maxSize, 0.2f, k * k);
                        transform.localScale = Flat ? new Vector3(sz, 0.08f, sz) : Vector3.one * sz;
                    }
                    _r.material.color = new Color(_c0.r, _c0.g, _c0.b, _c0.a * (1f - k));
                }
                if (_t >= _life) Destroy(gameObject);
            }
        }
    }
}
