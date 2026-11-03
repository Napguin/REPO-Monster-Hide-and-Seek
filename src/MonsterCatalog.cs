using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Collects the real enemy prefabs of R.E.P.O. (from the EnemyDirector) and their sounds,
    /// so the seeker can look and sound like an actual monster of the game.
    /// </summary>
    internal static class MonsterCatalog
    {
        internal class Entry
        {
            public string Name;
            public GameObject Prefab;
            public List<AudioClip> RoarClips = new List<AudioClip>();
            public List<AudioClip> AttackClips = new List<AudioClip>();
            // The game's own sound objects (play through R.E.P.O.'s audio system / mixer)
            public List<Sound> RoarSounds = new List<Sound>();
            public List<Sound> AttackSounds = new List<Sound>();
        }

        private static List<Entry> _entries;

        // Enemies that are too tiny / have no real body to be fun as a player monster.
        private static readonly string[] Excluded = { "gnome", "banger", "bang", "hidden", "shadow child" };

        private static readonly string[] RoarWords = { "roar", "scream", "notice", "growl", "chase", "aggro", "yell", "shout", "hiss", "laugh" };
        private static readonly string[] AttackWords = { "attack", "bite", "slash", "hit", "swing", "stab", "kill", "grab", "eat" };

        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (_entries == null || _entries.Count == 0) Build();
                return _entries;
            }
        }

        public static Entry Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static void Invalidate() => _entries = null;

        private static void Build()
        {
            _entries = new List<Entry>();
            var dir = EnemyDirector.instance;
            if (dir == null) return;

            var setups = new List<EnemySetup>();
            void AddAll(List<EnemySetup> l) { if (l != null) setups.AddRange(l.Where(s => s != null)); }
            try
            {
                AddAll(dir.enemiesDifficulty1);
                AddAll(dir.enemiesDifficulty2);
                AddAll(dir.enemiesDifficulty3);
                AddAll(dir.enemyList);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Enemy list read failed: " + ex.Message); }

            var seen = new HashSet<GameObject>();
            foreach (var setup in setups.Distinct())
            {
                if (setup.spawnObjects == null) continue;
                foreach (var pref in setup.spawnObjects)
                {
                    GameObject prefab = null;
                    try { prefab = pref?.Prefab; }
                    catch (Exception ex) { Plugin.Log.LogDebug("Prefab load failed: " + ex.Message); }
                    if (prefab == null || !seen.Add(prefab)) continue;

                    var parent = prefab.GetComponentInChildren<EnemyParent>(true);
                    if (parent == null) continue; // director / helper objects
                    string name = !string.IsNullOrEmpty(parent.enemyName) ? parent.enemyName : prefab.name.Replace("Enemy - ", "").Trim();
                    if (Excluded.Any(x => name.ToLowerInvariant().Contains(x))) continue;
                    if (_entries.Any(e => e.Name == name)) continue;

                    var entry = new Entry { Name = name, Prefab = prefab };
                    CollectSounds(prefab, entry);
                    _entries.Add(entry);
                }
            }
            _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo($"Monster catalog: {_entries.Count} monsters");
            foreach (var e in _entries)
                Plugin.Log.LogInfo($"  - {e.Name} (prefab '{e.Prefab.name}', roar sounds {e.RoarClips.Count}, attack sounds {e.AttackClips.Count})");
        }

        private static void CollectSounds(GameObject prefab, Entry entry)
        {
            var all = new List<AudioClip>();
            var allSounds = new List<Sound>();
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                foreach (var f in mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType != typeof(Sound)) continue;
                    Sound s;
                    try { s = f.GetValue(mb) as Sound; } catch { continue; }
                    if (s?.Sounds == null) continue;
                    var clips = s.Sounds.Where(c => c != null).ToList();
                    if (clips.Count == 0) continue;
                    string n = f.Name.ToLowerInvariant();
                    if (RoarWords.Any(n.Contains)) { entry.RoarClips.AddRange(clips); entry.RoarSounds.Add(s); }
                    if (AttackWords.Any(n.Contains)) { entry.AttackClips.AddRange(clips); entry.AttackSounds.Add(s); }
                    all.AddRange(clips);
                    if (clips.Any(c => c.length > 0.6f)) allSounds.Add(s);
                }
            }
            if (entry.RoarClips.Count == 0) entry.RoarClips.AddRange(all.Where(c => c.length > 0.6f).Take(6));
            if (entry.AttackClips.Count == 0) entry.AttackClips.AddRange(entry.RoarClips);
            if (entry.RoarSounds.Count == 0) entry.RoarSounds.AddRange(allSounds.Take(4));
            if (entry.AttackSounds.Count == 0) entry.AttackSounds.AddRange(entry.RoarSounds);
        }
    }
}
