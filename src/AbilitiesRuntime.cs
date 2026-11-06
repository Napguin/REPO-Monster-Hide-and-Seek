using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>Special abilities of the monsters (everything except the laser, which has its own code).</summary>
    public partial class HideAndSeekManager
    {
        private const int MsgSpecial = 10;

        // local seeker
        private float _sprintUntil, _revealUntil;
        // all clients
        private float _leapUntil;
        private float _stunUntil;
        private readonly List<Projectile> _projectiles = new List<Projectile>();

        // echo roar: hiders within range at the moment of the roar (local seeker shows direction arrows)
        private const float EchoRange = 15f, EchoTime = 1.5f;
        private readonly List<Vector3> _echoTargets = new List<Vector3>();
        private float _echoUntil;

        private void OnRoarEcho(Vector3 roarPos)
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            if (localSeeker)
            {
                _echoTargets.Clear();
                foreach (var h in AliveHiders())
                    if (FlatDistance(PlayerPos(h), roarPos) <= EchoRange) _echoTargets.Add(PlayerPos(h));
                foreach (var d in AliveDummies())
                    if (FlatDistance(d.transform.position, roarPos) <= EchoRange) _echoTargets.Add(d.transform.position);
                _echoUntil = Time.time + EchoTime;
                Banner(_echoTargets.Count > 0 ? L.EchoFound(_echoTargets.Count) : L.EchoNothing, new Color(1f, 0.6f, 0.3f), 1.5f);
                Plugin.Log.LogInfo($"[Monster] echo: {_echoTargets.Count} hider(s) within {EchoRange}m");
                return;
            }
            var local = GameRefs.LocalAvatar;
            if (local != null && IsAlive(local) && FlatDistance(PlayerPos(local), roarPos) <= EchoRange)
            {
                _flashUntil = Time.time + 0.6f;
                Banner(L.Echoed, new Color(1f, 0.5f, 0.3f), 2f);
            }
        }

        /// <summary>Arrows at the screen edge pointing towards the hiders found by the echo (no distance).</summary>
        private void DrawEchoArrows(float sw, float sh, float u)
        {
            if (Time.time >= _echoUntil || _echoTargets.Count == 0) return;
            var view = GameRefs.ViewTransform(Seeker);
            if (view == null) return;
            Vector3 me = SeekerPosition();
            float yaw = view.eulerAngles.y;
            float k = (_echoUntil - Time.time) / EchoTime;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(54 * u), fontStyle = FontStyle.Bold };
            Vector2 c = new Vector2(sw / 2f, sh / 2f);
            float rx = sw * 0.40f, ry = sh * 0.38f;
            foreach (var t in _echoTargets)
            {
                Vector3 local = Quaternion.Euler(0, -yaw, 0) * (t - me);
                float ang = Mathf.Atan2(local.x, local.z); // 0 = straight ahead, clockwise
                Vector2 pos = c + new Vector2(Mathf.Sin(ang) * rx, -Mathf.Cos(ang) * ry);
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang * Mathf.Rad2Deg, pos);
                GUI.color = new Color(1f, 0.55f, 0.2f, Mathf.Clamp01(k * 1.5f) * (0.75f + 0.25f * Mathf.Sin(Time.time * 14f)));
                GUI.Label(new Rect(pos.x - 40 * u, pos.y - 40 * u, 80 * u, 80 * u), "▲", style);
                GUI.matrix = m;
            }
            GUI.color = Color.white;
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
                    if (localSeeker) _sprintUntil = Time.time + Abilities.Duration(ability);
                    if (_monster != null) _monster.PlayRoar();
                    break;
                case AbilityType.Leap:
                    _leapUntil = Time.time + Abilities.Duration(ability);
                    if (_monster != null) _monster.PlayAttack();
                    if (localSeeker) DoLeap(dir);
                    break;
                case AbilityType.Reveal:
                    if (localSeeker) _revealUntil = Time.time + Abilities.Duration(ability);
                    else _flashUntil = Time.time + 0.6f;
                    if (_monster != null) _monster.PlayRoar();
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

            // host: during a leap the monster kills with a bigger reach
            if (IsHost && Phase == Phase.Hunting && Time.time < _leapUntil && Seeker != null)
            {
                Vector3 sp = SeekerPosition();
                foreach (var h in AliveHiders())
                    if (FlatDistance(sp, PlayerPos(h)) <= 2.4f && Mathf.Abs(PlayerPos(h).y - sp.y) < 2.5f) HostKill(h);
                foreach (var d in AliveDummies())
                    if (FlatDistance(sp, d.transform.position) <= 2.4f) KillDummy(d, sp, "leap");
            }

            // local hider: stunned by a shockwave
            if (Time.time < _stunUntil)
                Compat.Call(PlayerController.instance, "OverrideSpeed", 0.25f, 0.2f);
        }

        private void ClearAbilities()
        {
            foreach (var p in _projectiles) p.Destroy();
            _projectiles.Clear();
            _sprintUntil = _revealUntil = _leapUntil = _stunUntil = _echoUntil = 0f;
            _echoTargets.Clear();
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
            if (bestH != null) HostKill(bestH);
            if (bestD != null) { KillDummy(bestD, origin, "shot"); _lastAttackInfo = L.AbilityName(AbilityType.Shot) + ": " + bestD.DummyName; }
        }

        private void HostProjectileHits(Projectile p)
        {
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, p.Position) < 1.1f) { HostKill(h); p.Dead = true; return; }
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, p.Position) < 1.1f)
                {
                    KillDummy(d, p.Position, "throw");
                    _lastAttackInfo = L.AbilityName(AbilityType.Throw) + ": " + d.DummyName;
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

            var targets = new List<(Vector3 pos, string name)>();
            foreach (var h in AliveHiders()) targets.Add((PlayerPos(h) + Vector3.up * 1.9f, NameOf(h)));
            foreach (var d in AliveDummies()) targets.Add((d.transform.position + Vector3.up * 2f, d.DummyName));
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
                (BindName(_attackAction), L.Attack, _attackReadyAt, Plugin.AttackCooldown.Value, new Color(1f, 0.35f, 0.3f)),
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
