using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Bowtie – Tornado: a small tornado wanders forward, sucks in items and valuables around it and whirls them
    /// up and around. When it fades away it flings everything outward – flying items hurt hiders (like Item Throw).
    /// Hiders close to it are pulled in a little and slowed, but not trapped.
    /// Every client moves its own copy of the tornado (same start values); the host moves the items.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private readonly List<Tornado> _tornados = new List<Tornado>();

        private void SpawnTornado(Vector3 origin)
        {
            Vector3 fwd = MonsterForward();
            Vector3 start = (Seeker != null ? PlayerPos(Seeker) : origin) + fwd * 2.5f;
            // stand on the floor
            if (Physics.Raycast(start + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) start = hit.point;
            _tornados.Add(new Tornado(start, fwd * Abilities.TornadoSpeed, Time.time + Abilities.Duration(AbilityType.Tornado),
                Abilities.UnityColor(AbilityType.Tornado)));
            if (_monster != null) _monster.PlayRoar();
            Plugin.Log.LogInfo($"[Tornado] spawned at {start}");
        }

        private float _tornadoScanAt, _tornadoPushAt;

        private void TornadoTick()
        {
            for (int i = _tornados.Count - 1; i >= 0; i--)
            {
                var t = _tornados[i];
                bool alive = t.Step(Time.deltaTime) && Phase == Phase.Hunting;
                if (IsHost) { HostTornadoItems(t, alive); HostTornadoPlayers(t, alive); }
                if (!alive)
                {
                    Effects.Shockwave(t.Pos + Vector3.up, 6f, t.Color, 0.5f);
                    t.Destroy();
                    _tornados.RemoveAt(i);
                }
            }
            if (_tornados.Count == 0) return;

            if (!LocalHider(out var me)) return;
            Vector3 p = PlayerPos(me);
            // trapped: the host spins the ragdoll; locally we only stop grabbing
            foreach (var t in _tornados)
            {
                if (!t.LocalTrapped && FlatDistance(p, t.Pos) <= Abilities.TornadoTrapRange && Mathf.Abs(p.y - t.Pos.y) < 3f)
                {
                    t.LocalTrapped = true;
                    Banner(L.TornadoTrapped, t.Color, 2f);
                }
                if (!t.LocalTrapped) continue;
                var grabber = Compat.Get<Component>(me, "physGrabber");
                if (grabber != null) Compat.Call(grabber, "OverrideGrabDisable", 0.3f);
                return;
            }
            // close by: pulled in a bit and slowed, can still run away
            foreach (var t in _tornados)
            {
                float d = FlatDistance(p, t.Pos);
                if (d > Abilities.TornadoPullRange || Mathf.Abs(p.y - t.Pos.y) > 4f) continue;
                Compat.Call(PlayerController.instance, "OverrideSpeed", d < 2.5f ? 0.55f : 0.8f, 0.2f);
                if (Time.time >= _tornadoPushAt)
                {
                    _tornadoPushAt = Time.time + 0.25f;
                    Vector3 to = -Away(t.Pos, p);
                    Vector3 swirl = Vector3.Cross(Vector3.up, to); // a little sideways, like it's spinning
                    LocalPush((to * 1.6f + swirl * 0.8f) * (1f - d / Abilities.TornadoPullRange));
                }
                break;
            }
        }

        /// <summary>Host: catch items near the tornado and whirl them; fling them out when it's over.</summary>
        private void HostTornadoItems(Tornado t, bool alive)
        {
            t.Caught.RemoveAll(rb => rb == null);
            if (!alive)
            {
                foreach (var rb in t.Caught)
                {
                    rb.useGravity = true;
                    Vector3 outDir = Away(t.Pos, rb.worldCenterOfMass) + Vector3.up * 0.6f;
                    rb.velocity = outDir.normalized * Abilities.TornadoFlingSpeed;
                    rb.angularVelocity = Random.insideUnitSphere * 12f;
                    TrackFlyingItem(rb, 2f);
                }
                t.Caught.Clear();
                return;
            }

            if (t.Caught.Count < Abilities.TornadoMaxItems && Time.time >= _tornadoScanAt)
            {
                _tornadoScanAt = Time.time + 0.3f;
                foreach (var o in Object.FindObjectsOfType<PhysGrabObject>())
                {
                    if (o == null || o.isEnemy || o.isPlayer || o.isCart) continue;
                    var rb = o.GetComponent<Rigidbody>();
                    if (rb == null || rb.isKinematic || rb == _heldItem || t.Caught.Contains(rb) || InAnyTornado(rb)) continue;
                    Vector3 c = rb.worldCenterOfMass;
                    if (FlatDistance(c, t.Pos) > Abilities.TornadoCatchRange || c.y < t.Pos.y - 1f || c.y > t.Pos.y + 4.5f) continue;
                    rb.useGravity = false;
                    t.Caught.Add(rb);
                    if (t.Caught.Count >= Abilities.TornadoMaxItems) break;
                }
            }

            for (int k = 0; k < t.Caught.Count; k++)
            {
                var rb = t.Caught[k];
                // each item on its own orbit: different angle, height and radius
                float phase = k * 2.399f; // golden angle
                float ang = phase + (Time.time - t.Born) * 4f;
                float h = 0.6f + Mathf.Repeat(k * 0.7f + (Time.time - t.Born) * 0.5f, 3f);
                float r = 0.8f + h * 0.45f; // wider at the top – tornado shape
                Vector3 target = t.Pos + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);
                Vector3 to = target - rb.worldCenterOfMass;
                if (to.magnitude > 8f) { rb.useGravity = true; t.Caught.RemoveAt(k--); continue; } // stuck behind a wall – let go
                rb.velocity = Vector3.ClampMagnitude(to * 8f, 20f);
                rb.angularVelocity = new Vector3(3f, 6f, 2f);
                Compat.Call(rb.GetComponent<PhysGrabObject>(), "OverrideIndestructible", 0.2f); // whirling doesn't break it, the fling can
            }
        }

        /// <summary>
        /// Host: hiders that get too close are knocked into the ragdoll and spun around inside the tornado
        /// (the host runs ragdoll physics), then flung out when it fades. Dummies are carried around too.
        /// </summary>
        private void HostTornadoPlayers(Tornado t, bool alive)
        {
            if (!alive)
            {
                foreach (var h in t.Players)
                {
                    if (h == null) continue;
                    var tumble = Compat.Get<Component>(h, "tumble");
                    var rb = tumble != null ? Compat.Get<Rigidbody>(tumble, "rb") : null;
                    if (tumble != null) Compat.Call(tumble, "TumbleOverrideTime", 1f);
                    if (rb != null && !rb.isKinematic)
                        rb.velocity = (Away(t.Pos, rb.position) + Vector3.up * 0.7f).normalized * Abilities.TornadoFlingSpeed * 0.8f;
                }
                foreach (var d in t.Dummies)
                    if (d != null) d.transform.position = new Vector3(d.transform.position.x, t.Pos.y, d.transform.position.z) + Away(t.Pos, d.transform.position) * 2.5f;
                t.Players.Clear(); t.Dummies.Clear();
                return;
            }

            foreach (var h in AliveHiders())
                if (!t.Players.Contains(h) && FlatDistance(PlayerPos(h), t.Pos) <= Abilities.TornadoTrapRange && Mathf.Abs(PlayerPos(h).y - t.Pos.y) < 3f)
                {
                    t.Players.Add(h);
                    Plugin.Log.LogInfo($"[Tornado] trapped player {h.photonView.ViewID}");
                }
            foreach (var d in AliveDummies())
                if (!t.Dummies.Contains(d) && FlatDistance(d.transform.position, t.Pos) <= Abilities.TornadoTrapRange) t.Dummies.Add(d);

            float age = Time.time - t.Born;
            for (int k = 0; k < t.Players.Count; k++)
            {
                var h = t.Players[k];
                if (h == null || !IsAlive(h)) continue;
                var tumble = Compat.Get<Component>(h, "tumble");
                if (tumble == null) continue;
                var rb = Compat.Get<Rigidbody>(tumble, "rb");
                bool tumbling = Compat.Get<bool>(h, "isTumbling") || Compat.Get<bool>(tumble, "isTumbling");
                Compat.Call(tumble, "TumbleOverrideTime", 0.5f);
                if (!tumbling || rb == null || rb.isKinematic)
                {
                    if (Time.time >= t.RequestAt) { t.RequestAt = Time.time + 0.4f; Compat.Call(tumble, "TumbleSet", true, false); }
                    continue;
                }
                rb.velocity = Vector3.ClampMagnitude((OrbitPoint(t, k + 0.5f, age, 1.4f) - rb.position) * 6f, 14f);
            }
            for (int k = 0; k < t.Dummies.Count; k++)
            {
                var d = t.Dummies[k];
                if (d == null) continue;
                d.Stun(0.3f, false);
                d.transform.position = Vector3.Lerp(d.transform.position, OrbitPoint(t, k + 0.25f, age, 1.4f) - Vector3.up * 0.9f, 6f * Time.deltaTime);
            }
        }

        /// <summary>Point on the tornado's spiral for slot k (items and players use the same shape).</summary>
        private static Vector3 OrbitPoint(Tornado t, float k, float age, float extraRadius = 0f)
        {
            float ang = k * 2.399f + age * 4f;
            float h = 0.6f + Mathf.Repeat(k * 0.7f + age * 0.5f, 3f);
            float r = 0.8f + h * 0.45f + extraRadius;
            return t.Pos + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);
        }

        private bool InAnyTornado(Rigidbody rb)
        {
            foreach (var t in _tornados) if (t.Caught.Contains(rb)) return true;
            return false;
        }

        private void ClearTornados()
        {
            foreach (var t in _tornados)
            {
                foreach (var rb in t.Caught) if (rb != null) rb.useGravity = true;
                t.Destroy();
            }
            _tornados.Clear();
        }
    }

    internal class Tornado
    {
        public Vector3 Pos;
        private Vector3 _vel;
        private readonly float _until;
        public readonly float Born;
        public readonly Color Color;
        public readonly List<Rigidbody> Caught = new List<Rigidbody>();
        public readonly List<PlayerAvatar> Players = new List<PlayerAvatar>();
        public readonly List<TestDummy> Dummies = new List<TestDummy>();
        public bool LocalTrapped;
        public float RequestAt;
        private readonly GameObject _root;
        private readonly List<Renderer> _rings = new List<Renderer>();
        private const int Rings = 7;

        public Tornado(Vector3 pos, Vector3 vel, float until, Color color)
        {
            Pos = pos; _vel = vel; _until = until; Born = Time.time; Color = color;
            _root = new GameObject("HNS_Tornado");
            _root.transform.position = pos;
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            for (int i = 0; i < Rings; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                go.transform.SetParent(_root.transform, false);
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    if (sh != null) r.material = new Material(sh);
                    r.material.color = new Color(color.r, color.g, color.b, 0f);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _rings.Add(r);
                }
            }
        }

        /// <summary>Returns false when the tornado is over.</summary>
        public bool Step(float dt)
        {
            if (Time.time >= _until) return false;
            // wander forward, bounce off walls, slight random drift
            Vector3 step = _vel * dt;
            if (step.sqrMagnitude > 0f && Physics.SphereCast(Pos + Vector3.up * 0.8f, 0.6f, step.normalized, out var hit, step.magnitude + 0.6f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y < 0.5f)
            {
                var n = hit.normal; n.y = 0f;
                _vel = Vector3.Reflect(_vel, n.sqrMagnitude > 0.001f ? n.normalized : -_vel.normalized);
            }
            else Pos += step;
            // follow the floor (stairs / slopes)
            if (Physics.Raycast(Pos + Vector3.up * 1.2f, Vector3.down, out var fh, 3f, ~0, QueryTriggerInteraction.Ignore)) Pos.y = Mathf.MoveTowards(Pos.y, fh.point.y, 4f * dt);
            _vel = Quaternion.Euler(0f, Mathf.Sin((Time.time - Born) * 1.3f) * 40f * dt, 0f) * _vel;

            if (_root == null) return true;
            _root.transform.position = Pos;
            float age = Time.time - Born, left = _until - Time.time;
            float fade = Mathf.Clamp01(age / 0.5f) * Mathf.Clamp01(left / 0.6f);
            bool near = Effects.NearCamera(Pos + Vector3.up * 1.5f, 2.5f);
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                if (r == null) continue;
                float k = i / (float)(Rings - 1);
                float w = Mathf.Lerp(0.5f, 2.6f, k * k) * fade;
                float wob = Mathf.Sin(age * 5f + i * 0.9f) * 0.25f * k;
                var tr = r.transform;
                tr.localPosition = new Vector3(Mathf.Cos(age * 7f + i) * wob, 0.2f + k * 3.6f, Mathf.Sin(age * 7f + i) * wob);
                tr.localScale = new Vector3(w, 0.35f, w);
                tr.localRotation = Quaternion.Euler(0f, age * 540f + i * 40f, 0f);
                r.material.color = new Color(Color.r, Color.g, Color.b, 0.28f * fade);
                r.enabled = !near; // camera inside it would tint the whole screen
            }
            return true;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
