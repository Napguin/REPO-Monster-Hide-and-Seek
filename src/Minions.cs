using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Swarm (Gnome / Banger): the special summons 5 copies of the monster that hunt hiders on their own.
    /// The host runs the AI and the damage and sends positions; every client shows the minions.
    /// Gnomes hit again and again, Bangers blow themselves up. Minions die after 30 s.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgMinionState = 12, MsgMinionDie = 13;
        private const int MinionCount = 5;
        private const float MinionLife = 30f;
        private const int GnomeDamage = 10, BangerDamage = 35;
        private const float BangerRadius = 3f;

        private class Minion
        {
            public int Id;
            public bool Banger;
            public GameObject Go;
            public Animator Anim;
            public Vector3 Pos, NetPos;
            public float Yaw, NetYaw;
            public float DieAt, NextHit, FuseAt = -1f, RepathAt;
            public Vector3[] Path = new Vector3[0];
            public int Corner;
            public Vector3 Offset;
            public bool Exploded;
        }

        private readonly List<Minion> _minions = new List<Minion>();
        private int _minionSerial;
        private float _minionSendAt;

        private bool MonsterIsBanger => (_monsterName ?? "").ToLowerInvariant().Contains("bang");

        /// <summary>Called on every client when the seeker uses Swarm (same order everywhere → same ids).</summary>
        // ---- real game enemies (Gnome / Banger) ----
        private static readonly HashSet<EnemyParent> _summoned = new HashSet<EnemyParent>();
        internal static bool IsSummoned(EnemyParent p) => p != null && _summoned.Contains(p);
        private readonly List<Vector3> _summonedLastPos = new List<Vector3>();

        /// <summary>Gnome / Banger use the game's own enemy as minions (own vision + pathfinding).</summary>
        private MonsterCatalog.Entry RealSwarmEntry()
        {
            var e = MonsterCatalog.Find(_monsterName);
            if (e == null || e.Setup == null || LevelGenerator.Instance == null) return null;
            string n = e.Name.ToLowerInvariant();
            return n.Contains("gnome") || n.Contains("bang") ? e : null;
        }

        private IEnumerator HostSpawnRealSwarm(MonsterCatalog.Entry entry, Vector3 center, Vector3 fwd)
        {
            // "Disable normal enemies" would despawn the new ones before we know they are ours
            _despawnPauseUntil = Time.time + 4f;
            var dir = EnemyDirector.instance;
            var before = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
            if (dir != null && dir.enemiesSpawned != null) before.UnionWith(dir.enemiesSpawned.Where(x => x != null));
            var directorsBefore = new HashSet<MonoBehaviour>(Object.FindObjectsOfType<MonoBehaviour>().Where(IsGroupDirector));

            var fresh = new List<EnemyParent>();
            for (int tries = 0; tries < 3 && fresh.Count < MinionCount; tries++)
            {
                try { LevelGenerator.Instance.EnemySpawn(entry.Setup, center); }
                catch (System.Exception ex) { Plugin.Log.LogWarning("[Swarm] EnemySpawn failed: " + ex.Message); yield break; }
                yield return new WaitForSeconds(0.3f);
                var now = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
                if (dir != null && dir.enemiesSpawned != null) now.UnionWith(dir.enemiesSpawned.Where(x => x != null));
                fresh = now.Where(p => !before.Contains(p)).ToList();
                foreach (var p in fresh.Take(MinionCount)) _summoned.Add(p);
            }
            if (fresh.Count == 0) { _despawnPauseUntil = 0f; Plugin.Log.LogWarning("[Swarm] no enemies appeared"); yield break; }

            // the group's director (moves the whole group): put it on the monster
            foreach (var d in Object.FindObjectsOfType<MonoBehaviour>().Where(IsGroupDirector).Where(d => !directorsBefore.Contains(d)))
                Compat.Call(d, "SetPosition", center);

            yield return new WaitForSeconds(0.3f);
            for (int i = 0; i < fresh.Count; i++)
            {
                var p = fresh[i];
                if (p == null) continue;
                if (i >= MinionCount) { _summoned.Remove(p); DestroyEnemy(p); continue; } // the game spawns groups – keep 5
                Vector3 pos = center + Quaternion.Euler(0, -60f + i * 30f, 0) * fwd * 1.5f;
                if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;
                _summoned.Add(p);
                Compat.Set(p, "firstSpawnPointUsed", true);
                Compat.Call(Compat.Get<Enemy>(p, "Enemy"), "EnemyTeleported", pos);
                Compat.Call(p, "SpawnedTimerSet", MinionLife + 10f);
                if (!Compat.Get(p, "Spawned", false)) Compat.Call(p, "Spawn");
            }
            _despawnPauseUntil = 0f;
            Plugin.Log.LogInfo($"[Swarm] {Mathf.Min(fresh.Count, MinionCount)} real '{entry.Name}' spawned");
            StartCoroutine(HostSwarmLifetime(fresh.Take(MinionCount).ToList()));
        }

        private static bool IsGroupDirector(MonoBehaviour m) =>
            m != null && (m.GetType().Name == "EnemyGnomeDirector" || m.GetType().Name == "EnemyBangDirector");

        private IEnumerator HostSwarmLifetime(List<EnemyParent> list)
        {
            float end = Time.time + MinionLife;
            while (Time.time < end && RoundActive) yield return new WaitForSeconds(0.5f);
            foreach (var p in list) KillSummoned(p);
        }

        private static void KillSummoned(EnemyParent p)
        {
            if (p == null) return;
            _summoned.Remove(p);
            var health = p.GetComponentInChildren<EnemyHealth>(true);
            if (health == null || !Compat.Call(health, "Death", Vector3.up)) Compat.Call(p, "Despawn");
        }

        private static void DestroyEnemy(EnemyParent p)
        {
            if (p == null) return;
            if (SemiFunc.IsMultiplayer() && Photon.Pun.PhotonNetwork.IsMasterClient)
            {
                var pv = p.GetComponentInParent<Photon.Pun.PhotonView>();
                if (pv != null) { Photon.Pun.PhotonNetwork.Destroy(pv.gameObject); return; }
            }
            Object.Destroy(p.transform.root.gameObject);
        }

        /// <summary>Host: a hider died close to one of our real minions → counts for the monster.</summary>
        private bool NearSummoned(Vector3 pos)
        {
            foreach (var p in _summoned)
            {
                if (p == null) continue;
                var e = Compat.Get<Enemy>(p, "Enemy");
                Vector3 ep = e != null ? e.transform.position : p.transform.position;
                if (Vector3.Distance(ep, pos) < 6f) return true;
            }
            return false;
        }

        private void OnSwarm(Vector3 origin)
        {
            Vector3 c = Seeker != null ? PlayerPos(Seeker) : origin;
            Vector3 fwd = MonsterForward();
            var real = RealSwarmEntry();
            if (real != null)
            {
                if (_monster != null) _monster.PlayRoar();
                if (IsHost) StartCoroutine(HostSpawnRealSwarm(real, c, fwd));
                return;
            }
            bool banger = MonsterIsBanger;
            for (int i = 0; i < MinionCount; i++)
            {
                Vector3 off = Quaternion.Euler(0, -60f + i * 30f, 0) * fwd * 1.5f;
                Vector3 p = c + off;
                if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) p = hit.position;
                var m = new Minion
                {
                    Id = ++_minionSerial,
                    Banger = banger,
                    Pos = p, NetPos = p,
                    Yaw = Quaternion.LookRotation(fwd).eulerAngles.y,
                    DieAt = Time.time + MinionLife,
                    NextHit = Time.time + 0.5f,
                    Offset = new Vector3(Random.Range(-0.6f, 0.6f), 0, Random.Range(-0.6f, 0.6f)),
                };
                m.NetYaw = m.Yaw;
                m.Go = _monster != null ? _monster.CloneBodyForMinion() : null;
                if (m.Go == null)
                {
                    m.Go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Object.Destroy(m.Go.GetComponent<Collider>());
                    m.Go.transform.localScale = Vector3.one * 0.5f;
                }
                m.Anim = m.Go.GetComponentInChildren<Animator>();
                m.Go.transform.position = p;
                _minions.Add(m);
                Effects.Shockwave(p + Vector3.up * 0.3f, 1.5f, Abilities.UnityColor(AbilityType.Swarm), 0.4f);
            }
            if (_monster != null) _monster.PlayRoar();
            Plugin.Log.LogInfo($"[Monster] swarm: {MinionCount} {(banger ? "bangers" : "minions")} summoned");
        }

        private void MinionTick()
        {
            if (_minions.Count == 0) return;
            float dt = Time.deltaTime;

            for (int i = _minions.Count - 1; i >= 0; i--)
            {
                var m = _minions[i];
                if (m.Go == null) { _minions.RemoveAt(i); continue; }
                if (Time.time >= m.DieAt) { KillMinion(m, false); continue; }

                bool moving;
                if (IsHost && Phase == Phase.Hunting) moving = HostMinionAI(m, dt);
                else
                {
                    Vector3 before = m.Pos;
                    m.Pos = Vector3.Lerp(m.Pos, m.NetPos, 1f - Mathf.Exp(-12f * dt));
                    m.Yaw = Mathf.LerpAngle(m.Yaw, m.NetYaw, 1f - Mathf.Exp(-12f * dt));
                    moving = (m.Pos - before).sqrMagnitude > 0.0001f;
                }
                if (m.Go == null) continue;
                m.Go.transform.position = m.Pos;
                m.Go.transform.rotation = Quaternion.Euler(0, m.Yaw, 0);
                AnimateMinion(m, moving);
                // banger fuse: blink
                if (m.FuseAt > 0f)
                {
                    float s = 1f + 0.15f * Mathf.Abs(Mathf.Sin(Time.time * 25f));
                    m.Go.transform.localScale = Vector3.one * s;
                }
            }

            if (IsHost && SemiFunc.IsMultiplayer() && Time.time >= _minionSendAt && _minions.Count > 0)
            {
                _minionSendAt = Time.time + 0.1f;
                var data = new float[_minions.Count * 5];
                for (int i = 0; i < _minions.Count; i++)
                {
                    var m = _minions[i];
                    data[i * 5] = m.Id; data[i * 5 + 1] = m.Pos.x; data[i * 5 + 2] = m.Pos.y; data[i * 5 + 3] = m.Pos.z; data[i * 5 + 4] = m.Yaw;
                }
                SendUnreliable(MsgMinionState, data);
            }
        }

        /// <summary>Host: chase the nearest hider (or dummy), attack, explode. Returns whether it moved.</summary>
        private bool HostMinionAI(Minion m, float dt)
        {
            // target
            Vector3? target = null;
            float best = float.MaxValue;
            PlayerAvatar targetPlayer = null;
            TestDummy targetDummy = null;
            foreach (var h in AliveHiders())
            {
                float d = Vector3.Distance(PlayerPos(h), m.Pos);
                if (d < best) { best = d; target = PlayerPos(h); targetPlayer = h; targetDummy = null; }
            }
            foreach (var dmy in AliveDummies())
            {
                float d = Vector3.Distance(dmy.transform.position, m.Pos);
                if (d < best) { best = d; target = dmy.transform.position; targetDummy = dmy; targetPlayer = null; }
            }
            if (target == null && Seeker != null) { target = PlayerPos(Seeker) + m.Offset * 3f; best = Vector3.Distance(target.Value, m.Pos); }
            if (target == null) return false;

            // attack
            if (m.Banger)
            {
                if (m.FuseAt < 0f && best < 1.6f && (targetPlayer != null || targetDummy != null)) m.FuseAt = Time.time + 0.8f;
                if (m.FuseAt > 0f)
                {
                    if (Time.time >= m.FuseAt) HostBangerExplode(m);
                    return false; // stands still while the fuse burns
                }
            }
            else if (best < 1.3f && Time.time >= m.NextHit && (targetPlayer != null || targetDummy != null))
            {
                m.NextHit = Time.time + 1f;
                if (targetPlayer != null) HostDamage(targetPlayer, GnomeDamage, "gnome");
                if (targetDummy != null) HostDamageDummy(targetDummy, GnomeDamage, m.Pos, "gnome");
                if (m.Anim != null) foreach (var p in m.Anim.parameters)
                        if (p.type == AnimatorControllerParameterType.Trigger && p.name.ToLowerInvariant().Contains("attack")) m.Anim.SetTrigger(p.nameHash);
            }
            if (best < 0.9f) return false;

            // walk along the navmesh
            if (Time.time >= m.RepathAt)
            {
                m.RepathAt = Time.time + 0.4f;
                var path = new NavMeshPath();
                Vector3 to = target.Value + m.Offset;
                if (NavMesh.SamplePosition(to, out var th, 2f, NavMesh.AllAreas)) to = th.position;
                if (NavMesh.CalculatePath(m.Pos, to, NavMesh.AllAreas, path) && path.corners.Length > 1) { m.Path = path.corners; m.Corner = 1; }
                else { m.Path = new[] { m.Pos, to }; m.Corner = 1; }
            }
            if (m.Corner >= m.Path.Length) return false;
            Vector3 next = m.Path[m.Corner];
            Vector3 step = next - m.Pos;
            if (step.magnitude < 0.2f) { m.Corner++; return true; }
            float speed = m.Banger ? 5f : 4.2f;
            Vector3 move = Vector3.ClampMagnitude(step, speed * dt);
            m.Pos += move;
            var flat = new Vector3(step.x, 0, step.z);
            if (flat.sqrMagnitude > 0.001f) m.Yaw = Mathf.LerpAngle(m.Yaw, Quaternion.LookRotation(flat).eulerAngles.y, 1f - Mathf.Exp(-10f * dt));
            return true;
        }

        private void HostBangerExplode(Minion m)
        {
            if (m.Exploded) return; // the die message comes back over the network a bit later
            m.Exploded = true;
            foreach (var h in AliveHiders())
                if (Vector3.Distance(PlayerPos(h), m.Pos) <= BangerRadius) HostDamage(h, BangerDamage, "banger");
            foreach (var d in AliveDummies())
                if (Vector3.Distance(d.transform.position, m.Pos) <= BangerRadius) HostDamageDummy(d, BangerDamage, m.Pos, "banger");
            Send(MsgMinionDie, m.Id, true, m.Pos);
        }

        private void KillMinion(Minion m, bool exploded)
        {
            if (m.Go != null)
            {
                if (exploded)
                {
                    Effects.Shockwave(m.Pos + Vector3.up * 0.4f, BangerRadius * 2.2f, new Color(1f, 0.7f, 0.3f), 0.45f);
                    if (LocalHider(out var me) && Vector3.Distance(PlayerPos(me), m.Pos) <= BangerRadius)
                    {
                        LocalPush(Away(m.Pos, PlayerPos(me)) * 7f + Vector3.up * 3.5f);
                        _flashUntil = Time.time + 0.4f;
                    }
                }
                else Effects.Shockwave(m.Pos + Vector3.up * 0.3f, 1.2f, new Color(0.6f, 0.6f, 0.65f), 0.35f);
                Object.Destroy(m.Go);
            }
            m.Go = null;
            _minions.Remove(m);
        }

        private void OnMinionState(float[] data)
        {
            for (int i = 0; i + 4 < data.Length; i += 5)
            {
                int id = (int)data[i];
                var m = _minions.FirstOrDefault(x => x.Id == id);
                if (m == null) continue;
                m.NetPos = new Vector3(data[i + 1], data[i + 2], data[i + 3]);
                m.NetYaw = data[i + 4];
            }
        }

        private void OnMinionDie(int id, bool exploded, Vector3 pos)
        {
            var m = _minions.FirstOrDefault(x => x.Id == id);
            if (m == null) return;
            m.Pos = pos;
            KillMinion(m, exploded);
        }

        private static void AnimateMinion(Minion m, bool moving)
        {
            if (m.Anim == null || m.Anim.runtimeAnimatorController == null) return;
            foreach (var p in m.Anim.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Bool) continue;
                string n = p.name.ToLowerInvariant();
                if (n.Contains("move") || n.Contains("walk")) m.Anim.SetBool(p.nameHash, moving);
            }
        }

        private void ClearMinions()
        {
            if (IsHost) foreach (var p in _summoned.ToList()) KillSummoned(p);
            _summoned.Clear();
            foreach (var m in _minions) if (m.Go != null) Object.Destroy(m.Go);
            _minions.Clear();
        }
    }
}
