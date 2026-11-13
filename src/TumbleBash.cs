using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Tick – Tumble-Bash: the Tick curls up and rolls around fast for a few seconds, bouncing off walls. The monster
    /// steers a little with the camera. Rolling into a hider knocks them over (ragdoll, pushed away) and hurts them.
    /// The monster moves its own body; the host decides hits and pushes the knocked-over hider (host runs ragdolls).
    /// </summary>
    public partial class HideAndSeekManager
    {
        private float _bashUntil, _bashFxAt, _bashSpeedMul = 1f, _bashBounceReadyAt;
        private Vector3 _bashDir;
        private readonly HashSet<int> _bashHit = new HashSet<int>();
        private bool Bashing => Time.time < _bashUntil;

        private void OnTumbleBash(Vector3 dir)
        {
            dir.y = 0f;
            _bashDir = dir.sqrMagnitude > 0.001f ? dir.normalized : MonsterForward();
            _bashUntil = Time.time + Abilities.Duration(AbilityType.TumbleBash);
            _bashHit.Clear();
            _bashSpeedMul = 1f;
            if (_monster != null) _monster.PlayAttack();
        }

        private void TumbleBashTick()
        {
            bool active = Bashing && Seeker != null && Phase == Phase.Hunting;
            if (_monster != null) _monster.RollAngle = active ? (Time.time * 900f) % 360f : 0f;
            if (!active) return;

            // ---- local monster: roll, steer a little with the camera, bounce off walls ----
            if (GameRefs.IsLocal(Seeker))
            {
                var pc = PlayerController.instance;
                var rb = Compat.Get<Rigidbody>(pc, "rb");
                Compat.Call(InputManager.instance, "DisableMovement", 0.2f);
                var view = GameRefs.ViewTransform(Seeker);
                if (view != null)
                {
                    var f = view.forward; f.y = 0f;
                    if (f.sqrMagnitude > 0.001f)
                    {
                        // turning has drag: it turns gradually and every turn costs speed (sharp turns more)
                        Vector3 before = _bashDir;
                        _bashDir = Vector3.RotateTowards(_bashDir, f.normalized, Abilities.BashSteer * Mathf.Deg2Rad * Time.deltaTime, 0f);
                        float turned = Vector3.Angle(before, _bashDir);
                        _bashSpeedMul = Mathf.Max(Abilities.BashTurnMinSpeed, _bashSpeedMul - turned * Abilities.BashTurnDrag);
                    }
                }
                if (rb != null)
                {
                    Vector3 from = rb.position + Vector3.up * 0.5f;
                    float step = Abilities.BashSpeed * _bashSpeedMul * Time.deltaTime + 0.45f;
                    if (Physics.SphereCast(from, 0.35f, _bashDir, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
                        && !IsPlayerCollider(hit.collider) && hit.normal.y < 0.5f && Time.time >= _bashBounceReadyAt) // max one bounce per 0.2 s
                    {
                        var n = hit.normal; n.y = 0f;
                        if (n.sqrMagnitude > 0.001f)
                        {
                            // bounce off at an angle, never straight back: head-on hits are turned to the side
                            n.Normalize();
                            float into = Mathf.Clamp01(-Vector3.Dot(_bashDir, n));
                            Vector3 slide = Vector3.ProjectOnPlane(_bashDir, n);
                            if (slide.sqrMagnitude < 0.09f) // (almost) head-on: pick a side
                            {
                                Vector3 side = Vector3.Cross(Vector3.up, n);
                                if (slide.sqrMagnitude > 0.0001f ? Vector3.Dot(slide, side) < 0f : Random.value < 0.5f) side = -side;
                                slide = side;
                            }
                            _bashDir = (slide.normalized + n * Abilities.BashBounce).normalized;
                            _bashSpeedMul = Mathf.Min(_bashSpeedMul, Mathf.Lerp(0.9f, Abilities.BashWallSlow, into));
                            _bashBounceReadyAt = Time.time + Abilities.BashBounceGap;
                        }
                        if (Time.time >= _bashFxAt) { _bashFxAt = Time.time + 0.15f; Effects.Shockwave(hit.point, 1.2f, Abilities.UnityColor(AbilityType.TumbleBash), 0.25f); }
                    }
                    _bashSpeedMul = Mathf.MoveTowards(_bashSpeedMul, 1f, 0.7f * Time.deltaTime); // ~1 s back to full speed
                    float spd = Abilities.BashSpeed * _bashSpeedMul;
                    rb.velocity = new Vector3(_bashDir.x * spd, rb.velocity.y, _bashDir.z * spd);
                }
            }

            // ---- host: hits ----
            if (!IsHost) return;
            Vector3 sp = PlayerPos(Seeker);
            float r = Abilities.BashHitRadius * Mathf.Max(1f, _roundScale);
            foreach (var h in AliveHiders())
            {
                if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.8f, sp + Vector3.up * 0.5f) > r || !_bashHit.Add(h.photonView.ViewID)) continue;
                HostDamage(h, Abilities.BashDamage, "tumble-bash");
                Vector3 away = Away(sp, PlayerPos(h)) + _bashDir * 0.6f;
                _hostLaunches.Add(new HostLaunchJob { Victim = h, Dir = away.normalized, Start = Time.time, Speed = Abilities.BashKnockback });
                Plugin.Log.LogInfo($"[Tumble-Bash] hit {h.photonView.ViewID}");
            }
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position, sp) <= r + 0.4f && _bashHit.Add(-1000 - d.GetInstanceID()))
                {
                    d.Stun(1.5f, false);
                    HostDamageDummy(d, Abilities.BashDamage, sp, "tumble-bash");
                }
        }
    }
}
