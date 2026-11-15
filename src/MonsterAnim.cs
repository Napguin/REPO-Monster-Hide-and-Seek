using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Which animation clip of a monster plays for which action. Without the enemy's own script the game's
    /// animator controllers can't be steered reliably, so the clips are played directly (see ClipAnimator).
    ///
    /// A role spec looks like  "Scream Intro > Scream@0-0.5*1.2~"
    ///   '>'      steps played one after another
    ///   '|'      alternatives: the first one that finds a clip wins
    ///   words    must all be in the clip name (case-insensitive), "!word" must not be; the shortest name wins
    ///   @a-b     only that part of the clip (0..1 of its length)
    ///   *s       playback speed (0 = hold the first frame)
    ///   ~        the last step loops
    /// Roles: idle walk run jump fall attack special charge | c-idle c-walk c-jump c-fall pickup throw (Rugrat).
    /// Flags: flip (model faces backwards), hidden (only visible while attacking / sprinting), bob=0.15 (hop).
    /// </summary>
    internal static class AnimProfiles
    {
        internal static readonly string[] Roles =
            { "idle", "walk", "run", "jump", "fall", "attack", "special", "charge", "c-idle", "c-walk", "c-jump", "c-fall", "pickup", "throw" };

        private static readonly Dictionary<string, string> Generic = new Dictionary<string, string>
        {
            { "idle", "idle !break !pickup !stun !float|idle !break !stun" },
            { "walk", "walk|move !intro !outro !slow !pickup|move !intro !outro !pickup|crawl !attack" },
            { "run", "run !intro !outro|chase !begin !bite" },
            { "jump", "jump !attack !pickup !impulse" },
            { "fall", "fall !attack !pickup|falling !attack !pickup" },
            { "attack", "attack !crawl !jump !falling !land !player !lookunder !charge !delay|melee|bite|chomp" },
        };

        // Built-in tuning per monster (lower-case game name).
        private static readonly Dictionary<string, string> BuiltIn = new Dictionary<string, string>
        {
            { "animal", "flip" },
            { "apex predator", "charge=duck trans > duck attack~" },
            { "elsa", "idle=big chase*0; walk=big chase; run=big chase; jump=big jump; fall=big falling; charge=big chase*1.3~" },
            { "reaper", "walk=runner - move !intro !outro; run=runner - move !intro !outro; charge=attack player~; attack=attack player; jump=runner - jump; fall=runner - falling" },
            { "rugrat", "walk=valuablethrower - move !slow !pickup; c-idle=pickup idle; c-walk=pickup move !slow; c-jump=pickup jump; c-fall=pickup fall; pickup=pickup intro; throw=pickup outro" },
            { "banger", "attack=bang fuse" },
            { "upscream", "special=upscream - attack" },
            { "bowtie", "special=scream intro > scream@0-0.5" },
            { "gambit", "walk=walk cycle; special=trap player@0-0.6" },
            { "mentalist", "special=chargeattack !delay > floater - attack" },
            { "birthday boy", "special=blow balloon" },
            { "chef", "charge=tumbler - tell > tumbler - tumble !end~; attack=tumbler - tumble !end@0-0.6; bob=0.15" },
            { "headman", "attack=chase bite|bite|chomp; charge=chase !begin !bite~|chase~" },
            { "headgrab", "attack=attack01|attack 01|attack@0-0.5; charge=attack01@0.5-1~|attack 01@0.5-1~|attack@0.5-1~" },
            { "robe", "charge=attack~" },
            { "clown", "attack=melee|kick; special=beam|laser|shoot|attack !melee~" },
            { "huntsman", "attack=shoot|fire|aim|attack; special=shoot|fire|aim|attack" },
            { "spewer", "special=vomit|puke|spew|attack~" },
            { "heart hugger", "attack=chomp|bite|attack" },
            { "trudge", "special=slam|attack@0.3-1" },
            { "cleanup crew", "attack=melee; special=throw|attack !melee" },
            { "bella", "hidden" },
        };

        // Overrides saved with the animation tuner (debug menu), per monster.
        private static Dictionary<string, Dictionary<string, string>> _user;
        internal static string UserFile => Path.Combine(BepInEx.Paths.ConfigPath, "HideAndSeekMonster.animations.cfg");

        internal class Profile
        {
            public readonly Dictionary<string, string> Specs = new Dictionary<string, string>();
            public bool Flip, Hidden;
            public float Bob;
        }

        public static Profile For(string monster)
        {
            var p = new Profile();
            foreach (var kv in Generic) p.Specs[kv.Key] = kv.Value;
            string key = (monster ?? "").ToLowerInvariant();
            if (BuiltIn.TryGetValue(key, out var b)) Apply(p, b);
            LoadUser();
            if (_user.TryGetValue(key, out var u))
                foreach (var kv in u) Apply(p, kv.Key + "=" + kv.Value);
            return p;
        }

        private static void Apply(Profile p, string line)
        {
            foreach (var part in line.Split(';'))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;
                int eq = s.IndexOf('=');
                string k = (eq < 0 ? s : s.Substring(0, eq)).Trim().ToLowerInvariant();
                string v = eq < 0 ? "" : s.Substring(eq + 1).Trim();
                if (k == "flip") p.Flip = v != "0" && v != "false";
                else if (k == "hidden") p.Hidden = v != "0" && v != "false";
                else if (k == "bob") float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.Bob);
                else if (v == "" || v == "-") p.Specs.Remove(k);
                else p.Specs[k] = v;
            }
        }

        private static void LoadUser()
        {
            if (_user != null) return;
            _user = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                if (!File.Exists(UserFile)) return;
                foreach (var raw in File.ReadAllLines(UserFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int bar = line.IndexOf('|');
                    if (bar < 0) continue;
                    string m = line.Substring(0, bar).Trim().ToLowerInvariant();
                    var rest = line.Substring(bar + 1);
                    if (!_user.TryGetValue(m, out var d)) _user[m] = d = new Dictionary<string, string>();
                    foreach (var part in rest.Split(';'))
                    {
                        int eq = part.IndexOf('=');
                        string k = (eq < 0 ? part : part.Substring(0, eq)).Trim().ToLowerInvariant();
                        if (k.Length == 0) continue;
                        d[k] = eq < 0 ? "1" : part.Substring(eq + 1).Trim();
                    }
                }
                Plugin.Log.LogInfo($"[Anim] loaded tuning for {_user.Count} monster(s) from {UserFile}");
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[Anim] could not read " + UserFile + ": " + ex.Message); }
        }

        /// <summary>Saves one role of a monster (tuner). Empty spec = back to the built-in value.</summary>
        public static void SaveUser(string monster, string role, string spec)
        {
            LoadUser();
            string m = (monster ?? "").ToLowerInvariant();
            if (!_user.TryGetValue(m, out var d)) _user[m] = d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(spec)) d.Remove(role); else d[role] = spec;
            try
            {
                var lines = new List<string>
                {
                    "# R.E.P.O. Hide 'n Seek – animation tuning (written by the F7 animation tuner).",
                    "# monster name | role=spec; role=spec   (send this file to the developer to make it the default)",
                };
                foreach (var kv in _user.Where(x => x.Value.Count > 0).OrderBy(x => x.Key))
                    lines.Add(kv.Key + " | " + string.Join("; ", kv.Value.Select(x => x.Key + "=" + x.Value)));
                File.WriteAllLines(UserFile, lines);
                Plugin.Log.LogInfo($"[Anim] saved {monster} {role}={spec}");
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[Anim] could not write " + UserFile + ": " + ex.Message); }
        }

        /// <summary>Logs every animation clip of every monster (for tuning).</summary>
        public static void DumpAll()
        {
            var sb = new System.Text.StringBuilder("[AnimDump] all monster animations:\n");
            foreach (var e in MonsterCatalog.Entries)
            {
                sb.Append("  ").Append(e.Name).Append(" (").Append(e.Prefab != null ? e.Prefab.name : "?").Append(")\n");
                if (e.Prefab == null) continue;
                foreach (var a in e.Prefab.GetComponentsInChildren<Animator>(true))
                {
                    var c = a.runtimeAnimatorController;
                    if (c == null) continue;
                    sb.Append("    animator ").Append(a.name).Append(" / ").Append(c.name).Append(": ")
                      .Append(string.Join(", ", c.animationClips.Where(x => x != null).GroupBy(x => x.name).Select(g => $"{g.Key} ({g.First().length:0.00}s{(g.First().isLooping ? " loop" : "")})"))).Append('\n');
                }
                foreach (var a in e.Prefab.GetComponentsInChildren<Animation>(true))
                {
                    var names = new List<string>();
                    foreach (AnimationState st in a) if (st != null && st.clip != null) names.Add($"{st.clip.name} ({st.clip.length:0.00}s)");
                    sb.Append("    legacy animation ").Append(a.name).Append(": ").Append(string.Join(", ", names)).Append('\n');
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ---------------- spec parsing ----------------

        internal class Step
        {
            public AnimationClip Clip;
            public int Input;
            public float A, B = 1f, Speed = 1f;
            public string Text;
            public float Seconds => Speed <= 0f ? float.PositiveInfinity : Mathf.Max(0.05f, (B - A) * Clip.length / Speed);
        }

        internal class Track
        {
            public string Role;
            public List<Step> Steps = new List<Step>();
            public bool Loop;
        }

        private static readonly Regex StepRx = new Regex(@"^(?<k>.*?)(?:@(?<a>[0-9.]+)\s*-\s*(?<b>[0-9.]+))?(?:\*(?<s>[0-9.]+))?\s*(?<l>~)?\s*$");

        public static Track Parse(string role, string spec, IList<AnimationClip> clips)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            var t = new Track { Role = role };
            foreach (var stepText in spec.Split('>'))
            {
                Step found = null;
                foreach (var alt in stepText.Split('|'))
                {
                    var m = StepRx.Match(alt.Trim());
                    if (!m.Success) continue;
                    var clip = FindClip(m.Groups["k"].Value, clips);
                    if (clip == null) continue;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    found = new Step { Clip = clip, Input = clips.IndexOf(clip), Text = alt.Trim() };
                    if (m.Groups["a"].Success) { found.A = Mathf.Clamp01(float.Parse(m.Groups["a"].Value, inv)); found.B = Mathf.Clamp01(float.Parse(m.Groups["b"].Value, inv)); }
                    if (found.B <= found.A) found.B = Mathf.Min(1f, found.A + 0.05f);
                    if (m.Groups["s"].Success) found.Speed = float.Parse(m.Groups["s"].Value, inv);
                    if (m.Groups["l"].Success) t.Loop = true;
                    break;
                }
                if (found != null) t.Steps.Add(found);
            }
            return t.Steps.Count > 0 ? t : null;
        }

        public static AnimationClip FindClip(string query, IList<AnimationClip> clips)
        {
            string q = query.Trim().ToLowerInvariant();
            if (q.Length == 0) return null;
            var exact = clips.FirstOrDefault(c => c.name.ToLowerInvariant() == q);
            if (exact != null) return exact;
            var words = q.Split(' ').Where(w => w.Length > 0).ToArray();
            var need = words.Where(w => w[0] != '!').ToArray();
            var not = words.Where(w => w[0] == '!' && w.Length > 1).Select(w => w.Substring(1)).ToArray();
            if (need.Length == 0) return null;
            // "BIG Chase" must not match "big chase" inside "bigchaser": compare without spaces only for joined words
            return clips.Where(c =>
                {
                    string n = c.name.ToLowerInvariant(), flat = n.Replace(" ", "");
                    return need.All(w => n.Contains(w) || flat.Contains(w)) && !not.Any(w => n.Contains(w) || flat.Contains(w));
                })
                .OrderBy(c => c.name.Length).FirstOrDefault();
        }
    }

    /// <summary>
    /// Plays a monster's animation clips directly through a PlayableGraph (idle / walk / run / jump / fall,
    /// attack, special, charge loops, Rugrat carrying), with short cross-fades.
    /// </summary>
    internal class ClipAnimator
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly List<AnimationClipPlayable> _inputs = new List<AnimationClipPlayable>();
        private float[] _weights;
        public readonly List<AnimationClip> Clips;
        private readonly AnimProfiles.Profile _profile;
        private readonly Dictionary<string, AnimProfiles.Track> _tracks = new Dictionary<string, AnimProfiles.Track>();
        private readonly string _monster;

        // what plays right now
        private AnimProfiles.Track _cur;
        private int _step;
        private float _t;
        private bool _finished;

        // one-shot action (attack / special / pickup / throw)
        private AnimProfiles.Track _action;
        private float _actionUntil;
        private int _actionFrame = -1;

        // tuner preview
        private AnimProfiles.Track _preview;

        public bool Charging, Carrying;
        private bool _wasCarrying;
        public bool ActionPlaying => _action != null;

        private ClipAnimator(Animator anim, List<AnimationClip> clips, AnimProfiles.Profile profile, string monster)
        {
            Clips = clips;
            _profile = profile;
            _monster = monster;
            anim.fireEvents = false; // the clips call methods of the enemy's scripts, which aren't there
            _graph = PlayableGraph.Create("HNS_" + monster);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "HNS", anim);
            _mixer = AnimationMixerPlayable.Create(_graph, clips.Count);
            output.SetSourcePlayable(_mixer);
            for (int i = 0; i < clips.Count; i++)
            {
                var p = AnimationClipPlayable.Create(_graph, clips[i]);
                p.SetApplyFootIK(false);
                p.SetSpeed(0);
                _graph.Connect(p, 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);
                _inputs.Add(p);
            }
            _weights = new float[clips.Count];
            Rebuild();
        }

        /// <summary>Takes over the animator, or returns null when the monster's clips don't fit (old behaviour stays).</summary>
        public static ClipAnimator TryCreate(Animator anim, string monster, AnimProfiles.Profile profile)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return null;
            try
            {
                var clips = anim.runtimeAnimatorController.animationClips.Where(c => c != null && !c.legacy)
                    .GroupBy(c => c.name).Select(g => g.First()).ToList();
                if (clips.Count == 0) return null;
                var idle = AnimProfiles.Parse("idle", profile.Specs.TryGetValue("idle", out var s1) ? s1 : null, clips);
                var walk = AnimProfiles.Parse("walk", profile.Specs.TryGetValue("walk", out var s2) ? s2 : null, clips);
                if (idle == null && walk == null) { Plugin.Log.LogInfo($"[Anim] '{monster}': no idle/walk clip – keeping the game's animator"); return null; }
                var ca = new ClipAnimator(anim, clips, profile, monster);
                anim.runtimeAnimatorController = null; // the graph drives the model from now on
                return ca;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[Anim] '{monster}': clip player failed ({ex.Message}) – keeping the game's animator");
                return null;
            }
        }

        public void Rebuild()
        {
            _tracks.Clear();
            var log = new List<string>();
            foreach (var role in AnimProfiles.Roles)
            {
                if (!_profile.Specs.TryGetValue(role, out var spec)) continue;
                var t = AnimProfiles.Parse(role, spec, Clips);
                if (t == null) continue;
                _tracks[role] = t;
                log.Add(role + "=" + string.Join(" > ", t.Steps.Select(s => s.Clip.name + (s.A > 0 || s.B < 1 ? $"@{s.A:0.##}-{s.B:0.##}" : ""))) + (t.Loop ? "~" : ""));
            }
            _cur = null;
            Plugin.Log.LogInfo($"[Anim] '{_monster}' clip player: " + string.Join("; ", log));
        }

        public bool Has(string role) => _tracks.ContainsKey(role);
        public string Current => _preview != null ? "preview" : _action != null ? _action.Role : _cur != null ? _cur.Role : "-";
        public AnimProfiles.Track TrackOf(string role) => _tracks.TryGetValue(role, out var t) ? t : null;

        /// <summary>Plays a one-shot role. A looping one keeps going for 'duration' seconds.</summary>
        public bool PlayAction(string role, float duration = 0f)
        {
            if (!_tracks.TryGetValue(role, out var t)) return false;
            // a special started this frame wins over the attack that some specials also trigger
            if (role == "attack" && _actionFrame == Time.frameCount && _action != null) return true;
            _action = t;
            _actionFrame = Time.frameCount;
            if (t.Loop && duration <= 0f) duration = t.Steps.Sum(x => float.IsInfinity(x.Seconds) ? 1f : x.Seconds);
            _actionUntil = t.Loop ? Time.time + duration : float.PositiveInfinity;
            Start(t);
            return true;
        }

        public void Preview(AnimProfiles.Track t)
        {
            _preview = t;
            if (t != null) Start(t);
        }

        private void Start(AnimProfiles.Track t)
        {
            _cur = t;
            _step = 0;
            _t = 0f;
            _finished = false;
        }

        public void Tick(float dt, float speed, bool grounded, float vy)
        {
            if (!_graph.IsValid()) return;

            if (Carrying != _wasCarrying)
            {
                _wasCarrying = Carrying;
                PlayAction(Carrying ? "pickup" : "throw");
            }

            if (_preview != null && _cur == _preview && _finished) _preview = null;
            if (_action != null && (_cur != _action || (_finished && !_action.Loop) || Time.time >= _actionUntil))
                _action = null;

            float rate = 1f;
            if (_preview == null && _action == null)
            {
                string c = Carrying ? "c-" : "";
                AnimProfiles.Track want = null;
                if (Charging) want = TrackOf("charge") ?? TrackOf("run");
                if (want == null && !grounded && vy > 0.8f) want = TrackOf(c + "jump") ?? TrackOf("jump");
                if (want == null && !grounded && vy < -1.5f) want = TrackOf(c + "fall") ?? TrackOf("fall");
                if (want == null && speed > 4.5f && !Carrying && Has("run")) { want = TrackOf("run"); rate = Mathf.Clamp(speed / 6f, 0.8f, 1.6f); }
                if (want == null && speed > 0.3f) { want = TrackOf(c + "walk") ?? TrackOf("walk") ?? TrackOf("run"); rate = Mathf.Clamp(speed / 3f, 0.6f, 1.8f); }
                if (want == null) want = TrackOf(c + "idle") ?? TrackOf("idle") ?? TrackOf("walk");
                if (want != null && want != _cur) Start(want);
                if (want == TrackOf("charge")) rate = 1f;
            }
            if (_cur == null) return;

            // advance the current step
            var st = _cur.Steps[Mathf.Clamp(_step, 0, _cur.Steps.Count - 1)];
            if (!_finished)
            {
                _t += dt * rate;
                while (_t >= st.Seconds)
                {
                    if (_step < _cur.Steps.Count - 1) { _t -= st.Seconds; _step++; st = _cur.Steps[_step]; }
                    else if (_cur.Loop || IsLoco(_cur)) { _t -= st.Seconds; if (st.Seconds <= 0.0001f) break; }
                    else { _t = st.Seconds; _finished = true; break; }
                }
            }
            float frac = float.IsInfinity(st.Seconds) ? 0f : Mathf.Clamp01(_t / st.Seconds);
            _inputs[st.Input].SetTime((st.A + (st.B - st.A) * frac) * st.Clip.length);

            // cross-fade towards the current clip
            float k = Mathf.Clamp01(dt / 0.15f);
            float sum = 0f;
            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] = Mathf.MoveTowards(_weights[i], i == st.Input ? 1f : 0f, k);
                sum += _weights[i];
            }
            for (int i = 0; i < _weights.Length; i++) _mixer.SetInputWeight(i, sum > 0f ? _weights[i] / sum : 0f);
            _graph.Evaluate(0f);
        }

        private static bool IsLoco(AnimProfiles.Track t) =>
            t.Role == "idle" || t.Role == "walk" || t.Role == "run" || t.Role == "c-idle" || t.Role == "c-walk";

        public void Destroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
