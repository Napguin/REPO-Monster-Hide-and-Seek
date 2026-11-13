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
        private float _bashUntil, _bashFxAt;
        private Vector3 _bashDir;
        private readonly HashSet<int> _bashHit = new HashSet<int>();
        private bool Bashing => Time.time < _bashUntil;

        private void OnTumbleBash(Vector3 dir)
        {
            dir.y = 0f;
            _bashDir = dir.sqrMagnitude > 0.001f ? dir.normalized : MonsterForward();
            _bashUntil = Time.time + Abilities.Duration(AbilityType.TumbleBash);
            _bashHit.Clear();
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
                        _bashDir = Vector3.RotateTowards(_bashDir, f.normalized, Abilities.BashSteer * Mathf.Deg2Rad * Time.deltaTime, 0f);
                }
                if (rb != null)
                {
                    Vector3 from = rb.position + Vector3.up * 0.5f;
                    float step = Abilities.BashSpeed * Time.deltaTime + 0.45f;
                    if (Physics.SphereCast(from, 0.35f, _bashDir, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
                        && !IsPlayerCollider(hit.collider) && hit.normal.y < 0.5f)
                    {
                        var n = hit.normal; n.y = 0f;
                        if (n.sqrMagnitude > 0.001f) _bashDir = Vector3.Reflect(_bashDir, n.normalized).normalized;
                        if (Time.time >= _bashFxAt) { _bashFxAt = Time.time + 0.15f; Effects.Shockwave(hit.point, 1.2f, Abilities.UnityColor(AbilityType.TumbleBash), 0.25f); }
                    }
                    rb.velocity = new Vector3(_bashDir.x * Abilities.BashSpeed, rb.velocity.y, _bashDir.z * Abilities.BashSpeed);
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
