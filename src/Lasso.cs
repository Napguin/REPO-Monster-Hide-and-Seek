using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Pony – Lasso Grab: throws a lasso where the camera looks. A hit hider is reeled in towards the monster at a
    /// steady speed for 5 s – they can still move sideways, but they keep getting pulled in. The monster is very slow
    /// while it holds the rope. The host decides the hit; the caught hider moves their own (normal) body.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgLasso = 22;
        private const int LassoMiss = -1, LassoDummy = -2;

        private Vector3 _lassoOrigin, _lassoDir;
        private float _lassoFlyStart, _lassoMaxDist, _lassoUntil, _lassoMissHideAt;
        private bool _lassoFlying;
        private PlayerAvatar _lassoVictim;
        private TestDummy _lassoDummy, _lassoPendingDummy;
        private LineRenderer _lassoLine;

        private bool LassoHolding => Time.time < _lassoUntil && (_lassoVictim != null || _lassoDummy != null);

        private void OnLassoThrow(Vector3 origin, Vector3 dir)
        {
            _lassoOrigin = origin;
            _lassoDir = dir;
            _lassoFlyStart = Time.time;
            RayToWall(origin, dir, Abilities.LassoRange, out _lassoMaxDist);
            _lassoFlying = true;
            _lassoVictim = null; _lassoDummy = null; _lassoUntil = 0f;
            if (_monster != null) _monster.PlayAttack();
            if (!IsHost) return;

            // host: first hider / dummy along the rope's path (before a wall)
            PlayerAvatar bestH = null; TestDummy bestD = null; float best = _lassoMaxDist;
            foreach (var h in AliveHiders())
            {
                Vector3 c = PlayerPos(h) + Vector3.up * 0.9f;
                float along = Vector3.Dot(c - origin, dir);
                if (along < 0 || along > best) continue;
                if (Vector3.Distance(origin + dir * along, c) < Abilities.LassoHitRadius) { best = along; bestH = h; bestD = null; }
            }
            foreach (var d in AliveDummies())
            {
                Vector3 c = d.transform.position + Vector3.up * 0.9f;
                float along = Vector3.Dot(c - origin, dir);
                if (along < 0 || along > best) continue;
                if (Vector3.Distance(origin + dir * along, c) < Abilities.LassoHitRadius) { best = along; bestD = d; bestH = null; }
            }
            float flight = best / Abilities.LassoSpeed;
            if (bestH != null)
            {
                int id = bestH.photonView.ViewID;
                Later(flight, () => Send(MsgLasso, id));
            }
            else if (bestD != null)
            {
                _lassoPendingDummy = bestD;
                Later(flight, () => Send(MsgLasso, LassoDummy));
            }
            else Later(flight, () => Send(MsgLasso, LassoMiss));
            Plugin.Log.LogInfo($"[Lasso] thrown, {(bestH != null ? "hits player " + bestH.photonView.ViewID : bestD != null ? "hits a dummy" : "misses")} after {best:0.0} m");
        }

        private void OnLassoResult(int id)
        {
            _lassoFlying = false;
            if (id == LassoMiss) { _lassoMissHideAt = Time.time + 0.25f; return; }
            if (id == LassoDummy) { _lassoDummy = _lassoPendingDummy; _lassoPendingDummy = null; }
            else _lassoVictim = AliveHiders().FirstOrDefault(h => h.photonView != null && h.photonView.ViewID == id);
            if (_lassoVictim == null && _lassoDummy == null) return;
            _lassoUntil = Time.time + Abilities.Duration(AbilityType.Lasso);
            var me = GameRefs.LocalAvatar;
            if (_lassoVictim != null && me == _lassoVictim) Banner(L.Lassoed, Abilities.UnityColor(AbilityType.Lasso), 2f);
            if (_monster != null) _monster.PlayRoar();
        }

        private void LassoTick()
        {
            Vector3 hand = _monster != null ? _monster.MuzzlePosition : (Seeker != null ? PlayerPos(Seeker) + Vector3.up * 1.2f : _lassoOrigin);
            bool holding = LassoHolding && Seeker != null && Phase == Phase.Hunting;
            if (!holding && (_lassoVictim != null || _lassoDummy != null))
            {
                _lassoVictim = null; _lassoDummy = null; _lassoUntil = 0f;
            }
            if (_lassoVictim != null && !IsAlive(_lassoVictim)) { _lassoVictim = null; _lassoUntil = 0f; holding = false; }

            // ---- rope visual ----
            Vector3? end = null;
            if (_lassoFlying)
            {
                float d = Mathf.Min((Time.time - _lassoFlyStart) * Abilities.LassoSpeed, _lassoMaxDist);
                end = _lassoOrigin + _lassoDir * d;
                if (Time.time - _lassoFlyStart > Abilities.LassoRange / Abilities.LassoSpeed + 1f) _lassoFlying = false; // result lost
            }
            else if (holding && _lassoVictim != null) end = PlayerPos(_lassoVictim) + Vector3.up * 1f;
            else if (holding && _lassoDummy != null) end = _lassoDummy.transform.position + Vector3.up * 1f;
            else if (Time.time < _lassoMissHideAt) end = _lassoOrigin + _lassoDir * _lassoMaxDist;
            DrawLasso(hand, end);

            if (!holding) return;

            // ---- caught hider: reeled in at a steady speed, can still move sideways ----
            Vector3 sp = PlayerPos(Seeker);
            if (_lassoVictim != null && GameRefs.IsLocal(_lassoVictim))
            {
                var rb = Compat.Get<Rigidbody>(PlayerController.instance, "rb");
                Vector3 me = PlayerPos(_lassoVictim);
                Vector3 to = sp - me; to.y = 0f;
                float dist = to.magnitude;
                if (rb != null && dist > Abilities.LassoStopDistance)
                {
                    Vector3 step = to / dist * Abilities.LassoPullSpeed * Time.deltaTime;
                    // don't drag them through walls
                    if (!Physics.SphereCast(rb.position + Vector3.up * 0.6f, 0.3f, step.normalized, out _, step.magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                        rb.position += step;
                    // walking away from the monster only works sideways
                    var v = rb.velocity;
                    float away = -Vector3.Dot(new Vector3(v.x, 0f, v.z), to / dist);
                    if (away > 0f) rb.velocity = v + to / dist * away;
                }
            }
            // host: dummies are reeled in the same way
            if (IsHost && _lassoDummy != null)
            {
                Vector3 to = sp - _lassoDummy.transform.position; to.y = 0f;
                if (to.magnitude > Abilities.LassoStopDistance)
                    _lassoDummy.transform.position += to.normalized * Abilities.LassoPullSpeed * Time.deltaTime;
            }
        }

        private void DrawLasso(Vector3 hand, Vector3? end)
        {
            if (end == null) { if (_lassoLine != null) _lassoLine.enabled = false; return; }
            if (_lassoLine == null)
            {
                var go = new GameObject("HNS_Lasso");
                _lassoLine = go.AddComponent<LineRenderer>();
                _lassoLine.positionCount = 12;
                _lassoLine.startWidth = _lassoLine.endWidth = 0.04f;
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (sh != null) _lassoLine.material = new Material(sh);
                _lassoLine.startColor = _lassoLine.endColor = new Color(0.75f, 0.55f, 0.3f);
                _lassoLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _lassoLine.enabled = true;
            // slight sag in the middle, like a rope
            Vector3 e = end.Value;
            float sag = Mathf.Clamp(Vector3.Distance(hand, e) * 0.06f, 0.05f, 0.8f) * (LassoHolding ? 0.3f : 1f);
            for (int i = 0; i < 12; i++)
            {
                float k = i / 11f;
                _lassoLine.SetPosition(i, Vector3.Lerp(hand, e, k) - Vector3.up * sag * 4f * k * (1f - k));
            }
        }

        private void ClearLasso()
        {
            _lassoFlying = false;
            _lassoVictim = null; _lassoDummy = _lassoPendingDummy = null;
            _lassoUntil = _lassoMissHideAt = 0f;
            if (_lassoLine != null) _lassoLine.enabled = false;
        }
    }
}
