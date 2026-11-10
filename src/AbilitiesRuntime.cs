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
            Effects.Shockwave(roarPos + Vector3.up, ScanRange * 2f, new Color(1f, 0.6f, 0.3f), 0.9f);
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
            _specialReadyAt = Time.time + Abilities.Cooldown(ability);
            Plugin.Log.LogInfo($"[Monster] special pressed: {ability}");

            if (ability == AbilityType.Laser) { Send(MsgLaser, LocalAimDirection()); return; }

            Vector3 origin = _monster != null ? _monster.MuzzlePosition : SeekerPosition() + Vector3.up * 1.5f;
            Send(MsgSpecial, (int)ability, origin, LocalAimDirection());
        }

        // ------------------------------------------------------------------
        // Received on every client
        // ------------------------------------------------------------------
        private void OnSpecial(AbilityType ability, Vector3 origin, Vector3 dir)
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            string hiderMsg = L.AbilityUsedByMonster(ability);
            if (!localSeeker && !string.IsNullOrEmpty(hiderMsg)) Banner(hiderMsg, Abilities.UnityColor(ability), 2f);

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

            // local seeker: flying during Flying Charge (space = up, ctrl = down)
            if (Charging && _chargeType == AbilityType.FlyingCharge && Seeker != null && GameRefs.IsLocal(Seeker))
                Safe("fly", FlyTick);

            // local hider: stunned by a shockwave
            if (Time.time < _stunUntil)
                Compat.Call(PlayerController.instance, "OverrideSpeed", 0.25f, 0.2f);
        }

        private bool InChargeRadius(Vector3 seekerPos, Vector3 target)
        {
            float dy = Mathf.Abs(target.y - seekerPos.y);
            float r = Abilities.ChargeRadius * Mathf.Max(1f, _roundScale);
            return FlatDistance(seekerPos, target) <= r && dy < (_chargeType == AbilityType.FlyingCharge ? 3f : 2.5f);
        }

        private void FlyTick()
        {
            var pc = PlayerController.instance;
            if (pc == null) return;
            Compat.Call(pc, "AntiGravity", 0.25f);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            float up = 0f;
            if (kb != null && kb.spaceKey.isPressed) up += 1f;
            if (kb != null && (kb.leftCtrlKey.isPressed || kb.cKey.isPressed)) up -= 1f;
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
            Effects.Shockwave(center + Vector3.up, Abilities.RadarRange * 2f, col, 1.2f);
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
                (BindName(_attackAction), L.Attack, Charging ? Mathf.Max(_attackReadyAt, _chargeUntil) : _attackReadyAt,
                    Charging ? Abilities.Duration(_chargeType) : Plugin.AttackCooldown.Value, new Color(1f, 0.35f, 0.3f)),
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
            GUI.color = new Color(c.r, c.g, c.b, 0.18f + 0.08f * Mathf.Sin(Time.time * 12f));
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _white);
            GUI.color = Color.white;
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
        private readonly GameObject _visual;

        public Projectile(Vector3 pos, Vector3 vel, Color color)
        {
            Position = pos;
            Velocity = vel;
            Color = color;
            _visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = _visual.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            _visual.transform.localScale = Vector3.one * 0.45f;
            _visual.transform.position = pos;
            var r = _visual.GetComponent<Renderer>();
            if (r != null) r.material.color = color;
            var light = _visual.AddComponent<Light>();
            light.color = color;
            light.range = 3f;
            light.intensity = 2f;
        }

        public void Step(float dt)
        {
            Age += dt;
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
                r.material.color = new Color(color.r, color.g, color.b, 0.45f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.AddComponent<Fader>().Init(life, null, r, maxSize);
        }

        private class Fader : MonoBehaviour
        {
            private float _life, _t, _maxSize;
            private LineRenderer _lr;
            private Renderer _r;
            private Color _c0, _c1;

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
                    if (_maxSize > 0f) transform.localScale = Vector3.one * Mathf.Lerp(0.5f, _maxSize, Mathf.Sqrt(k));
                    _r.material.color = new Color(_c0.r, _c0.g, _c0.b, _c0.a * (1f - k));
                }
                if (_t >= _life) Destroy(gameObject);
            }
        }
    }
}
