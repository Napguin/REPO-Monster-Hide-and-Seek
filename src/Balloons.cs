using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Birthday Boy – Balloon Mines: each use places a balloon hovering in the air where the monster stands (max 5,
    /// the oldest pops when a 6th is placed). A hider coming close sets it off: 40 damage around it.
    /// Every client places the same balloons from the special message (same order → same ids); the host decides
    /// when one explodes and tells everyone.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgBalloon = 23;
        private readonly List<Balloon> _balloons = new List<Balloon>();
        private int _balloonNextId;
        private float _balloonCheckAt;
        private readonly List<float> _balloonRecharge = new List<float>(); // when a popped balloon's charge is back

        /// <summary>Balloons the monster can still place: 5 minus placed ones minus popped ones still recharging.</summary>
        // When the last hanging balloon pops, all of them come back together after 40 s.
        private float _balloonAllBackAt;
        internal int BalloonsLeft => Time.time < _balloonAllBackAt ? 0 : Abilities.BalloonMax - _balloons.Count - _balloonRecharge.Count(t => Time.time < t);
        internal float NextBalloonIn => Time.time < _balloonAllBackAt ? _balloonAllBackAt - Time.time
            : _balloonRecharge.Where(t => Time.time < t).DefaultIfEmpty(Time.time).Min() - Time.time;
        private bool BalloonsRecharging => Time.time < _balloonAllBackAt || _balloonRecharge.Any(t => Time.time < t);

        private void PlaceBalloon(Vector3 origin)
        {
            // use the position the monster player sent – other PCs' copy of the monster can be a bit behind
            Vector3 basePos = origin;
            Vector3 pos = basePos + Vector3.up * Abilities.BalloonHeight;
            if (Physics.Raycast(basePos + Vector3.up * 0.5f, Vector3.up, out var hit, Abilities.BalloonHeight + 0.4f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y - 0.5f; // low ceiling
            var b = new Balloon(_balloonNextId++, pos, Time.time + Abilities.BalloonArmTime);
            _balloons.Add(b);
            if (_monster != null) _monster.PlayAttack();
            if (Seeker != null && GameRefs.IsLocal(Seeker))
                Banner(L.BalloonsPlaced(BalloonsLeft, Abilities.BalloonMax), Abilities.UnityColor(AbilityType.BalloonMines), 1.2f);
        }

        private void BalloonTick()
        {
            // monster's ability bar: with no balloon left but one recharging, show that countdown
            if (Seeker != null && GameRefs.IsLocal(Seeker) && CurrentAbility == AbilityType.BalloonMines
                && BalloonsLeft <= 0 && BalloonsRecharging)
                _specialReadyAt = Mathf.Max(_specialReadyAt, Time.time + NextBalloonIn);
            if (_balloons.Count == 0) return;
            foreach (var b in _balloons) b.Animate();
            if (!IsHost || Phase != Phase.Hunting || Time.time < _balloonCheckAt) return;
            _balloonCheckAt = Time.time + 0.1f;
            foreach (var b in _balloons)
            {
                if (Time.time < b.ArmedAt) continue;
                bool trigger = false;
                foreach (var h in AliveHiders())
                    if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, b.Pos) <= Abilities.BalloonTriggerRange) { trigger = true; break; }
                if (!trigger)
                    foreach (var d in AliveDummies())
                        if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, b.Pos) <= Abilities.BalloonTriggerRange) { trigger = true; break; }
                if (!trigger) continue;
                Send(MsgBalloon, b.Id);
                return; // one per check, the list changes
            }
        }

        private void OnBalloonExplode(int id)
        {
            var b = _balloons.Find(x => x.Id == id);
            if (b == null) return;
            _balloons.Remove(b);
            b.Pop(true);
            _balloonRecharge.RemoveAll(t => Time.time >= t);
            if (_balloons.Count == 0)
            {
                // the last hanging balloon popped: all 5 come back together after 40 s
                _balloonRecharge.Clear();
            _balloonAllBackAt = 0f;
                _balloonAllBackAt = Time.time + Cd(Abilities.BalloonRecharge);
            }
            else _balloonRecharge.Add(Time.time + Cd(Abilities.BalloonReplace)); // others still hang: back after 3 s
            Effects.Shockwave(b.Pos, Abilities.BalloonRadius * 2f, b.Color, 0.4f);
            if (_monster != null) _monster.PlayRoar();
            if (LocalHider(out var me) && Vector3.Distance(PlayerPos(me) + Vector3.up * 0.9f, b.Pos) <= Abilities.BalloonRadius)
            {
                LocalPush(Away(b.Pos, PlayerPos(me)) * 6f + Vector3.up * 3f);
                _flashUntil = Time.time + 0.25f;
            }
            if (!IsHost || Phase != Phase.Hunting) return;
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, b.Pos) <= Abilities.BalloonRadius) HostDamage(h, Abilities.BalloonDamage, "balloon");
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, b.Pos) <= Abilities.BalloonRadius) HostDamageDummy(d, Abilities.BalloonDamage, b.Pos, "balloon");
        }

        private void ClearBalloons()
        {
            foreach (var b in _balloons) b.Pop(false);
            _balloons.Clear();
            _balloonNextId = 0;
            _balloonRecharge.Clear();
        }
    }

    internal class Balloon
    {
        public readonly int Id;
        public readonly Vector3 Pos;
        public readonly float ArmedAt;
        public readonly Color Color;
        private readonly GameObject _root, _ball;
        private readonly LineRenderer _string;
        private readonly float _seed;

        private static readonly Color[] Colors =
        {
            new Color(1f, 0.3f, 0.35f), new Color(0.3f, 0.6f, 1f), new Color(1f, 0.85f, 0.25f),
            new Color(0.45f, 0.9f, 0.4f), new Color(0.85f, 0.45f, 1f)
        };

        public Balloon(int id, Vector3 pos, float armedAt)
        {
            Id = id; Pos = pos; ArmedAt = armedAt;
            Color = Colors[id % Colors.Length];
            _seed = id * 1.7f;
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _root = new GameObject("HNS_Balloon");
            _root.transform.position = pos;
            _ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = _ball.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            _ball.transform.SetParent(_root.transform, false);
            _ball.transform.localScale = new Vector3(0.45f, 0.55f, 0.45f);
            var r = _ball.GetComponent<Renderer>();
            if (r != null)
            {
                if (sh != null) r.material = new Material(sh);
                r.material.color = new Color(Color.r, Color.g, Color.b, 0.6f); // a bit see-through until armed
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _string = _root.AddComponent<LineRenderer>();
            _string.positionCount = 2;
            _string.startWidth = _string.endWidth = 0.01f;
            if (sh != null) _string.material = new Material(sh);
            _string.startColor = _string.endColor = new Color(0.9f, 0.9f, 0.9f, 0.8f);
        }

        public void Animate()
        {
            if (_ball == null) return;
            float t = Time.time + _seed;
            Vector3 p = Pos + new Vector3(Mathf.Sin(t * 0.7f) * 0.05f, Mathf.Sin(t * 1.3f) * 0.08f, Mathf.Cos(t * 0.9f) * 0.05f);
            _ball.transform.position = p;
            _string.SetPosition(0, p - Vector3.up * 0.27f);
            _string.SetPosition(1, p - Vector3.up * 1.1f + new Vector3(Mathf.Sin(t) * 0.05f, 0f, 0f));
            var r = _ball.GetComponent<Renderer>();
            if (r != null && Time.time >= ArmedAt) r.material.color = Color;
        }

        public void Pop(bool exploded)
        {
            if (exploded) Effects.Shockwave(Pos, 1.2f, Color.white, 0.2f);
            if (_root != null) Object.Destroy(_root);
            if (_ball != null) Object.Destroy(_ball);
        }
    }
}
