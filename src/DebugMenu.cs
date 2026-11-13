using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Debug menu (F7, host only): revive / heal / teleport players, spawn real game enemies and training dummies,
    /// skip the hiding phase, end the round, reset cooldowns. Actions that change other players are sent as
    /// MsgDebug and done by every PC for its own player.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgDebug = 25;
        private const int DbgHeal = 1, DbgTeleport = 2, DbgSkipHide = 3, DbgResetCooldowns = 4;

        private bool _debugOpen;
        private int _debugEnemy;
        private Vector2 _debugScroll;
        private static readonly HashSet<EnemyParent> _debugSpawned = new HashSet<EnemyParent>();
        internal static bool IsDebugSpawned(EnemyParent p) => p != null && _debugSpawned.Contains(p);

        private void DebugMenuKey()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb[Plugin.DebugMenuKey.Value].wasPressedThisFrame && !GameRefs.IsTyping) _debugOpen = !_debugOpen;
        }

        // ---------------- actions ----------------

        private Vector3 HostFront(float dist)
        {
            var me = GameRefs.LocalAvatar;
            Vector3 p = me != null ? PlayerPos(me) : Vector3.zero;
            var cam = GameRefs.ViewTransform(me);
            Vector3 f = cam != null ? cam.forward : (me != null ? me.transform.forward : Vector3.forward);
            f.y = 0; if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
            Vector3 pos = p + f.normalized * dist;
            if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
            return pos;
        }

        private void DebugReviveAll()
        {
            int n = 0;
            foreach (var p in AllPlayers())
                if (!IsAlive(p) && Compat.Call(p, "Revive", true)) n++;
            Plugin.Log.LogInfo($"[Debug] revived {n} player(s)");
            Later(1.5f, () => Send(MsgDebug, DbgHeal, Vector3.zero));
            Banner(L.DebugRevived(n), Color.green, 2f);
        }

        private void OnDebug(int action, Vector3 pos)
        {
            var me = GameRefs.LocalAvatar;
            switch (action)
            {
                case DbgHeal:
                    if (me != null && me.playerHealth != null)
                    {
                        int hp = Compat.Get(me.playerHealth, "health", 100), max = Compat.Get(me.playerHealth, "maxHealth", 100);
                        if (!IsAlive(me)) Compat.Call(me, "Revive", true);
                        if (hp < max) Compat.Call(me.playerHealth, "Heal", max - hp, true);
                    }
                    break;
                case DbgTeleport:
                    if (me == null) break;
                    if (!IsAlive(me)) Compat.Call(me, "Revive", true);
                    // spread a little so nobody stands inside another player
                    int idx = Mathf.Abs(ActorOf(me)) % 8;
                    Vector3 p = pos + Quaternion.Euler(0, idx * 45f, 0) * Vector3.forward * (idx == 0 ? 0f : 1f) + Vector3.up * 0.2f;
                    TeleportLocal(p);
                    Later(0.5f, () => TeleportLocal(p));
                    break;
                case DbgSkipHide:
                    if (RoundActive && Phase == Phase.Hiding) _phaseEndTime = Time.time;
                    break;
                case DbgResetCooldowns:
                    _specialReadyAt = _roarReadyAt = _attackReadyAt = 0f;
                    _balloonRecharge.Clear();
                    break;
            }
        }

        private void DebugSpawnEnemy(MonsterCatalog.Entry entry)
        {
            if (entry?.Setup == null || LevelGenerator.Instance == null) { Banner(L.DebugCantSpawn, Color.white, 2f); return; }
            StartCoroutine(HostDebugSpawn(entry, HostFront(4f)));
        }

        private IEnumerator HostDebugSpawn(MonsterCatalog.Entry entry, Vector3 pos)
        {
            _despawnPauseUntil = Time.time + 4f; // "disable normal enemies" must not remove it right away
            var dir = EnemyDirector.instance;
            var before = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
            if (dir != null && dir.enemiesSpawned != null) before.UnionWith(dir.enemiesSpawned.Where(x => x != null));
            try { LevelGenerator.Instance.EnemySpawn(entry.Setup, pos); }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[Debug] EnemySpawn failed: " + ex.Message); yield break; }
            yield return new WaitForSeconds(0.3f);
            var now = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
            if (dir != null && dir.enemiesSpawned != null) now.UnionWith(dir.enemiesSpawned.Where(x => x != null));
            var fresh = now.Where(p => !before.Contains(p)).ToList();
            foreach (var p in fresh) _debugSpawned.Add(p);
            for (int round = 0; round < 3; round++)
            {
                foreach (var d in Object.FindObjectsOfType<MonoBehaviour>().Where(IsGroupDirector)) Compat.Call(d, "SetPosition", pos);
                for (int i = 0; i < fresh.Count; i++)
                {
                    var p = fresh[i];
                    if (p == null) continue;
                    Compat.Set(p, "firstSpawnPointUsed", true);
                    if (!Compat.Get(p, "Spawned", false)) Compat.Call(p, "Spawn");
                    var e = Compat.Get<Enemy>(p, "Enemy");
                    Vector3 spot = pos + Quaternion.Euler(0, i * 40f, 0) * Vector3.forward * (i == 0 ? 0f : 1.2f);
                    if (e != null && Vector3.Distance(e.transform.position, spot) > 2f) Compat.Call(e, "EnemyTeleported", spot);
                }
                yield return new WaitForSeconds(0.5f);
            }
            _despawnPauseUntil = 0f;
            Plugin.Log.LogInfo($"[Debug] spawned {fresh.Count} '{entry.Name}'");
        }

        private void DebugKillEnemies()
        {
            int n = 0;
            var dir = EnemyDirector.instance;
            var all = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
            if (dir != null && dir.enemiesSpawned != null) all.UnionWith(dir.enemiesSpawned.Where(x => x != null));
            foreach (var p in all)
            {
                if (p == null || !Compat.Get(p, "Spawned", false)) continue;
                var health = p.GetComponentInChildren<EnemyHealth>(true);
                if (health == null || !Compat.Call(health, "Death", Vector3.up)) Compat.Call(p, "Despawn");
                n++;
            }
            _debugSpawned.Clear();
            Plugin.Log.LogInfo($"[Debug] killed {n} enemies");
            Banner(L.DebugKilled(n), Color.white, 2f);
        }

        // ---------------- window ----------------

        private void DrawDebugMenu(float sw, float sh, float u)
        {
            EnsureMenuStyles(u);
            float w = Mathf.Min(460 * u, sw - 40 * u), h = Mathf.Min(sh - 80 * u, 820 * u);
            var r = new Rect(sw - w - 20 * u, (sh - h) / 2f, w, h);
            GUI.DrawTexture(r, Tex(new Color(0.06f, 0.06f, 0.08f, 0.97f)));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4 * u), Tex(new Color(0.3f, 0.6f, 1f, 1f)));
            GUI.Label(new Rect(r.x + 16 * u, r.y + 10 * u, r.width - 140 * u, 36 * u), "DEBUG  (F7)", _mSection);
            if (GUI.Button(new Rect(r.xMax - 116 * u, r.y + 10 * u, 100 * u, 32 * u), L.Close, _chBtn)) _debugOpen = false;

            var area = new Rect(r.x + 16 * u, r.y + 54 * u, r.width - 32 * u, r.height - 70 * u);
            if (!IsHost)
            {
                GUI.Label(area, L.DebugHostOnly, _mSmall);
                return;
            }
            float bh = 36 * u, g = 6 * u;
            var content = new Rect(0, 0, area.width - 18 * u, 900 * u);
            _debugScroll = GUI.BeginScrollView(area, _debugScroll, content);
            try
            {
                float y = 0, cw = content.width, half = (cw - g) / 2f;
                void Head(string t) { GUI.Label(new Rect(0, y, cw, 28 * u), t, _mSection); y += 30 * u; }
                bool Btn(string t, float x, float wid) => GUI.Button(new Rect(x, y, wid, bh), t, _chBtn);
                void Row() { y += bh + g; }

                Head(L.DebugPlayers);
                if (Btn(L.DebugReviveAll, 0, half)) DebugReviveAll();
                if (Btn(L.DebugHealAll, half + g, half)) Send(MsgDebug, DbgHeal, Vector3.zero);
                Row();
                if (Btn(L.DebugAllToMe, 0, half)) Send(MsgDebug, DbgTeleport, HostFront(1.5f));
                if (Btn(L.DebugAllToTruck, half + g, half)) Send(MsgDebug, DbgTeleport, TruckPosition());
                Row();

                Head(L.DebugRound);
                GUI.enabled = RoundActive && Phase == Phase.Hiding;
                if (Btn(L.DebugSkipHide, 0, half)) Send(MsgDebug, DbgSkipHide, Vector3.zero);
                GUI.enabled = RoundActive;
                if (Btn(L.Abort, half + g, half)) Send(MsgEnd, (int)Winner.Aborted);
                GUI.enabled = true;
                Row();
                if (Btn(L.DebugResetCooldowns, 0, cw)) Send(MsgDebug, DbgResetCooldowns, Vector3.zero);
                Row();

                Head(L.DebugEnemies);
                var list = MonsterCatalog.Entries;
                if (list.Count > 0)
                {
                    _debugEnemy = Mathf.Clamp(_debugEnemy, 0, list.Count - 1);
                    if (Btn("◀", 0, 44 * u)) _debugEnemy = (_debugEnemy - 1 + list.Count) % list.Count;
                    GUI.Label(new Rect(50 * u, y, cw - 100 * u, bh), $"<b>{list[_debugEnemy].Name}</b>", CenterStyleSmall(u));
                    if (Btn("▶", cw - 44 * u, 44 * u)) _debugEnemy = (_debugEnemy + 1) % list.Count;
                    Row();
                    GUI.enabled = InLevel();
                    if (Btn(L.DebugSpawnEnemy, 0, half)) DebugSpawnEnemy(list[_debugEnemy]);
                    GUI.enabled = true;
                    if (Btn(L.DebugKillEnemies, half + g, half)) DebugKillEnemies();
                    Row();
                }

                Head(L.DebugDummies);
                if (Btn(L.DebugSpawnDummy, 0, half)) SpawnDummyAt(HostFront(4f));
                if (Btn(L.DebugRemoveDummies, half + g, half)) ClearDummies();
                Row();

                GUI.color = new Color(1, 1, 1, 0.6f);
                GUI.Label(new Rect(0, y, cw, 80 * u), L.DebugHint, _mSmall);
                GUI.color = Color.white;
            }
            finally { GUI.EndScrollView(); }
        }

        private GUIStyle _mCenterSmall;
        private GUIStyle CenterStyleSmall(float u)
        {
            if (_mCenterSmall == null) _mCenterSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
            _mCenterSmall.fontSize = Mathf.RoundToInt(18 * u);
            return _mCenterSmall;
        }
    }
}
