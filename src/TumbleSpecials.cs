using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Specials that knock hiders into the game's tumble (ragdoll) and throw them around:
    ///  - Upscream "Launch": the closest hider tumbles and is launched in the monster's camera direction.
    ///  - Trudge "Impulse": hiders around tumble, get sucked in towards the monster, then a shockwave blasts them away.
    /// The tumbling body belongs to the hider, so each hider moves their own body; the host deals the damage.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgLaunch = 21;

        // local hider state
        private float _launchAt, _impulseEnd, _impulseBlastUntil;
        private Vector3 _launchDir, _impulseCenter;
        private bool _impulseCaught;

        private Component LocalTumble(out Rigidbody rb)
        {
            rb = null;
            var me = GameRefs.LocalAvatar;
            var tumble = me != null ? Compat.Get<Component>(me, "tumble") : null;
            if (tumble != null) rb = Compat.Get<Rigidbody>(tumble, "rb");
            return tumble;
        }

        // ---------------- Launch (Upscream) ----------------

        private void HostLaunch(Vector3 dir)
        {
            if (!IsHost || Seeker == null) return;
            Vector3 sp = PlayerPos(Seeker);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : MonsterForward();
            var h = AliveHiders().Where(x => Vector3.Distance(PlayerPos(x), sp) <= Abilities.LaunchRange)
                .OrderBy(x => Vector3.Distance(PlayerPos(x), sp)).FirstOrDefault();
            var d = AliveDummies().Where(x => Vector3.Distance(x.transform.position, sp) <= Abilities.LaunchRange)
                .OrderBy(x => Vector3.Distance(x.transform.position, sp)).FirstOrDefault();
            bool dummyCloser = d != null && (h == null || Vector3.Distance(d.transform.position, sp) < Vector3.Distance(PlayerPos(h), sp));
            if (dummyCloser)
            {
                d.Stun(1.5f, false);
                d.transform.position += new Vector3(dir.x, Mathf.Max(dir.y, 0f), dir.z) * 3f;
                HostDamageDummy(d, Abilities.LaunchDamage, sp, "launch");
                Send(MsgLaunch, 0, dir);
                return;
            }
            if (h == null) { Plugin.Log.LogInfo("[Launch] no hider within range"); Send(MsgLaunch, -1, dir); return; }
            Plugin.Log.LogInfo($"[Launch] launching {h.photonView.ViewID}");
            Send(MsgLaunch, h.photonView.ViewID, dir);
            _hostLaunches.Add(new HostLaunchJob { Victim = h, Dir = dir, Start = Time.time });
            var victim = h;
            Later(0.3f, () => { if (victim != null) HostDamage(victim, Abilities.LaunchDamage, "launch"); });
        }

        private void OnLaunch(int viewId, Vector3 dir)
        {
            bool localSeeker = Seeker != null && GameRefs.IsLocal(Seeker);
            if (viewId == -1)
            {
                if (localSeeker) { Banner(L.LaunchNone, Abilities.UnityColor(AbilityType.Launch), 1.2f); _specialReadyAt = Time.time + Cd(0.5f); }
                return;
            }
            if (_monster != null) _monster.PlayAttack();
            var me = GameRefs.LocalAvatar;
            if (me == null || me.photonView == null || me.photonView.ViewID != viewId) return;
            var tumble = LocalTumble(out _);
            if (tumble != null) Compat.Call(tumble, "TumbleRequest", true, false);
            _launchDir = dir.normalized;
            _launchStart = Time.time;
            _launchAt = Time.time + 0.15f;
            _launchPushed = 0f;
            _launchRetryAt = Time.time + 0.3f;
            Plugin.Log.LogInfo($"[Launch] I'm launched (tumble component {(tumble != null ? "found" : "MISSING")})");
            Banner(L.Launched, Abilities.UnityColor(AbilityType.Launch), 1.5f);
        }

        private float _launchStart, _launchApplyEnd, _launchRetryAt, _launchPushed, _launchLogAt;

        /// <summary>
        /// Like the Trudge pull (which works): keep setting the ragdoll's velocity every frame, but only count frames in
        /// which the ragdoll body can actually move (not kinematic). Done after 0.3 s of real pushing or 2 s timeout.
        /// </summary>
        private void LaunchTick()
        {
            if (_launchAt <= 0f || Time.time < _launchAt) return;
            if (Time.time > _launchStart + 2f) { Plugin.Log.LogInfo($"[Launch] timeout, pushed {_launchPushed:0.00}s"); _launchAt = 0f; return; }
            var tumble = LocalTumble(out var rb);
            Vector3 v = _launchDir * Abilities.LaunchSpeed;
            v.y = Mathf.Max(v.y + 3f, 5f); // always a bit up, so looking down doesn't push them into the floor
            if (tumble == null) { LocalPush(v * 0.6f); _launchAt = 0f; return; } // no ragdoll available – plain push

            Compat.Call(tumble, "TumbleOverrideTime", 1.2f); // stay a ragdoll while flying
            bool movable = rb != null && !rb.isKinematic;
            if (Time.time >= _launchLogAt)
            {
                _launchLogAt = Time.time + 0.2f;
                Plugin.Log.LogInfo($"[Launch] tick: rb={(rb != null)} kinematic={(rb != null ? rb.isKinematic.ToString() : "-")} speed={(rb != null ? rb.velocity.magnitude : 0f):0.0} pushed={_launchPushed:0.00}s");
            }
            if (!movable)
            {
                if (Time.time >= _launchRetryAt) { _launchRetryAt = Time.time + 0.3f; Compat.Call(tumble, "TumbleRequest", true, false); }
                return;
            }
            rb.velocity = v;
            _launchPushed += Time.deltaTime;
            if (_launchPushed >= 0.3f)
            {
                rb.angularVelocity = Random.insideUnitSphere * 6f;
                Plugin.Log.LogInfo("[Launch] done");
                _launchAt = 0f;
            }
        }

        // The host runs the physics of a knocked-over player (the hider's own velocity changes get overwritten),
        // so the host pushes the ragdoll: wait until it really tumbles, then keep its velocity for a few frames.
        private class HostLaunchJob { public PlayerAvatar Victim; public Vector3 Dir; public float Start, Pushed, LogAt, RequestAt, Speed = Abilities.LaunchSpeed; }
        private readonly List<HostLaunchJob> _hostLaunches = new List<HostLaunchJob>();

        private void HostLaunchTick()
        {
            if (!IsHost) { _hostLaunches.Clear(); return; }
            for (int i = _hostLaunches.Count - 1; i >= 0; i--)
            {
                var j = _hostLaunches[i];
                if (j.Victim == null || Time.time > j.Start + 2f)
                {
                    Plugin.Log.LogInfo($"[Launch] host: stopped, pushed {j.Pushed:0.00}s");
                    _hostLaunches.RemoveAt(i); continue;
                }
                var tumble = Compat.Get<Component>(j.Victim, "tumble");
                var rb = tumble != null ? Compat.Get<Rigidbody>(tumble, "rb") : null;
                bool tumbling = Compat.Get<bool>(j.Victim, "isTumbling") || (tumble != null && Compat.Get<bool>(tumble, "isTumbling"));
                if (Time.time >= j.LogAt)
                {
                    j.LogAt = Time.time + 0.2f;
                    Plugin.Log.LogInfo($"[Launch] host tick: tumbling={tumbling} rb={(rb != null)} kinematic={(rb != null ? rb.isKinematic.ToString() : "-")} speed={(rb != null ? rb.velocity.magnitude : 0f):0.0} pushed={j.Pushed:0.00}s");
                }
                if (tumble == null || rb == null) continue;
                if (!tumbling || rb.isKinematic)
                {
                    if (Time.time >= j.RequestAt) { j.RequestAt = Time.time + 0.4f; Compat.Call(tumble, "TumbleSet", true, false); }
                    continue;
                }
                Vector3 v = j.Dir * j.Speed;
                v.y = Mathf.Max(v.y + 3f, 5f);
                Compat.Call(tumble, "TumbleOverrideTime", 1.2f);
                rb.velocity = v;
                j.Pushed += Time.deltaTime;
                if (j.Pushed >= 0.3f)
                {
                    rb.angularVelocity = Random.insideUnitSphere * 6f;
                    Plugin.Log.LogInfo("[Launch] host: launched");
                    _hostLaunches.RemoveAt(i);
                }
            }
        }

        // ---------------- Impulse (Trudge) ----------------

        private void OnImpulse(Vector3 origin)
        {
            float dur = Abilities.Duration(AbilityType.Impulse);
            Vector3 center = Seeker != null ? PlayerPos(Seeker) : origin;
            var col = Abilities.UnityColor(AbilityType.Impulse);
            Effects.Implode(center + Vector3.up * 0.1f, Abilities.ImpulseRange * 2f, col, dur);
            if (_monster != null) _monster.PlayAttack();

            if (LocalHider(out var me) && InImpulse(center, PlayerPos(me)))
            {
                _impulseCaught = true;
                _impulseCenter = center;
                _impulseEnd = Time.time + dur;
                var tumble = LocalTumble(out _);
                if (tumble != null) Compat.Call(tumble, "TumbleRequest", true, false);
            }

            List<PlayerAvatar> victims = null;
            List<TestDummy> dummies = null;
            if (IsHost)
            {
                victims = AliveHiders().Where(h => InImpulse(center, PlayerPos(h))).ToList();
                dummies = AliveDummies().Where(d => InImpulse(center, d.transform.position)).ToList();
                foreach (var d in dummies) d.Stun(dur + 1f, false);
            }

            Later(dur, () =>
            {
                Vector3 c = Seeker != null ? PlayerPos(Seeker) : center;
                Effects.Shockwave(c + Vector3.up * 0.3f, Abilities.ImpulseRange * 1.6f, col, 0.5f);
                if (_monster != null) _monster.PlayRoar();
                if (_impulseCaught)
                {
                    _impulseCaught = false;
                    _impulseCenter = c;
                    _impulseBlastUntil = Time.time + 0.5f; // applied in ImpulseTick once the ragdoll is ready
                }
                if (!IsHost || Phase != Phase.Hunting) return;
                foreach (var h in victims) if (h != null && IsAlive(h)) HostDamage(h, Abilities.ImpulseDamage, "impulse");
                foreach (var d in dummies) if (d != null) HostDamageDummy(d, Abilities.ImpulseDamage, c, "impulse");
            });
        }

        private static bool InImpulse(Vector3 center, Vector3 p) =>
            FlatDistance(center, p) <= Abilities.ImpulseRange && Mathf.Abs(p.y - center.y) < 3f;

        private void ImpulseTick()
        {
            if (_impulseCaught)
            {
                if (Time.time > _impulseEnd + 1f) { _impulseCaught = false; return; } // blast never came (round ended)
                Vector3 c = Seeker != null ? PlayerPos(Seeker) : _impulseCenter;
                var tumble = LocalTumble(out var rb);
                if (tumble != null) Compat.Call(tumble, "TumbleOverrideTime", 0.4f);
                Vector3 p = rb != null ? rb.position : PlayerPos(GameRefs.LocalAvatar);
                Vector3 to = c - p; to.y = 0f;
                float dist = to.magnitude;
                Vector3 pull = dist > 1.5f ? to / dist * Mathf.Min(dist * 2.5f, 9f) : Vector3.zero; // stop right in front of it
                if (rb != null && !rb.isKinematic)
                {
                    var v = rb.velocity;
                    rb.velocity = new Vector3(pull.x, Mathf.Min(v.y, 2f), pull.z);
                }
                else Compat.Call(PlayerController.instance, "OverrideSpeed", 0.1f, 0.2f);
                return;
            }
            if (_impulseBlastUntil <= 0f) return;
            if (Time.time > _impulseBlastUntil) { _impulseBlastUntil = 0f; return; }
            {
                var tumble = LocalTumble(out var rb);
                Vector3 me = rb != null ? rb.position : PlayerPos(GameRefs.LocalAvatar);
                Vector3 v = Away(_impulseCenter, me) * Abilities.ImpulseBlastSpeed + Vector3.up * 6f;
                if (tumble != null && rb != null && !rb.isKinematic)
                {
                    Compat.Call(tumble, "TumbleOverrideTime", 1f);
                    rb.velocity = v;
                    rb.angularVelocity = Random.insideUnitSphere * 6f;
                }
                else LocalPush(v * 0.6f);
                _impulseBlastUntil = 0f;
            }
        }

        private void ClearTumbleSpecials()
        {
            _launchAt = _impulseBlastUntil = 0f;
            _hostLaunches.Clear();
            _impulseCaught = false;
        }
    }
}
