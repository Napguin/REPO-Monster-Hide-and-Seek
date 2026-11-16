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
            { "elsa", "init=small spawn > small transform | small spawn > big transform | big transform | small transform; idle=small transform@0.97-1*0; keepscale=Head Constraint Parent, ANIM SHR_head, *HEAD SHARED*; smooth=ANIM ROOT; acttime=0.28; walk=big chase; run=big chase; jump=big jump; fall=big falling; charge=big chase*1.3~; "
                    + "proc=idle ANIM BIG BODY UPPER:x:3" },  // big form only, breathing sway (no slap)  // big form only + a procedural slap
            { "reaper", "walk=runner - move !intro !outro; run=runner - move !intro !outro; charge=attack player~; attack=attack player; jump=runner - jump; fall=runner - falling" },
            { "rugrat", "walk=valuablethrower - move !slow !pickup; c-idle=pickup idle; c-walk=pickup move !slow; c-jump=pickup jump; c-fall=pickup fall; pickup=pickup intro; throw=pickup outro" },
            { "banger", "attack=bang fuse" },
            { "upscream", "special=upscream - attack" },
            { "bowtie", "special=scream intro > scream@0-0.5" },
            { "gambit", "walk=walk cycle; attack=open mouth; special=open mouth > trap player@0-0.6; lockroot" },
            { "mentalist", "special=floater - chargeattack > floater - attack" },
            { "birthday boy", "special=blow balloon" },
            { "chef", "charge=tumbler - tell~; bob=0.15" },
            { "headman", "noclips" }, // animated by its own code system (clips are empty) – left as the game shows it
            { "headgrab", "walk=headgrabber - move !intro !outro !fast !head; run=move fast !intro !outro; attack=attack 01@0-0.5; charge=attack 01~" },
            { "robe", "walk=robe targetplayer|robe idle; charge=robe attack~; attack=robe attack" },
            { "clown", "walk=beamer - move !intro !outro !fast; run=move fast; attack=beamer - melee; special=attack intro > beamer - attack !intro !outro~" },
            { "huntsman", "walk=hunter - move !intro !outro; attack=hunter - aim > hunter - shoot@0-0.4; special=hunter - aim > hunter - shoot@0-0.4" },
            { "trudge", "walk=slow walker - move !intro !outro; attack=slow walker - attack@0.3-0.7; special=slow walker - attack@0.3-0.7" },
            { "cleanup crew", "walk=bomb thrower - move !intro !outro !fast !head; run=move fast !intro !outro; attack=bomb thrower - melee !torso; special=bomb thrower - attack !torso" },
            { "bella", "hidepart=Whole mesh CODE Main*, Hand ANIM*, Feet ANIM*, Foot ANIM*; proc=spin rotation_transform:x:300, spin pedal_crank:x:250" },
            { "loom", "noclips" },
            { "oogly", "proc=hover @root:y:0.1, idle Hand Left Main*:x:6, idle Finger1_Index_Main:x:10:1, idle Finger1_middle_Main:x:10:2, idle Finger1_little_Main:x:10:3, idle Thumb1_Main:x:10:1.5, chomp Hand Left Main*:x:-30" },
            { "snatcher", Pony },
            { "pony", Pony },
            { "spewer", "proc=hover @root:y:0.12, idle Head:z:5, idle Start Source:x:15:0, idle Mid Source:x:12:1, idle End Source:x:12:2, spec enemy slow mouth flying top:x:30, spec enemy slow mouth flying bot:x:-30, chomp enemy slow mouth flying top:x:25, chomp enemy slow mouth flying bot:x:-25" },
            { "heart hugger", "proc=idle Segment 1:z:4:0, idle Segment 2:z:5:1, idle Segment 3:z:6:2, idle Segment 4:z:6:3, idle Segment 5:x:5:1, idle Arm Left Segment 1:z:10:0, idle Arm Right Segment 1:z:10:2, breathe Breathe Scale Parent 1::0.04, breathe Breathe Scale Parent 2::0.04:1, chomp Head Segment 1:y:-30, chomp Head Segment 2:y:30, spec Head Segment 1:y:-20, spec Head Segment 2:y:20" },
            { "peeper", "noclips" },
        };

        // gallop: both front legs nearly together, both hind legs half a stride later, the body rocks with the stride
        private const string Pony = "proc=swing L Front Upper Leg Main:x:38:0, swing R Front Upper Leg Main:x:38:30, swing L Hind Thigh Main:x:34:180, swing R Hind Thigh Main:x:34:210, "
            + "swing L Front Forearm Main:x:30:70, swing R Front Forearm Main:x:30:100, swing L Hind Gaskin Main:x:26:250, swing R Hind Gaskin Main:x:26:280, "
            + "swing @root:x:2:90, bounce @root:y:0.03, swing Neck Main:x:4:270, idle Neck Main:x:3, act @root:x:-20, act Neck Main:x:-25, chomp Head Main:x:-35";

        // Overrides saved with the animation tuner (debug menu), per monster.
        private static Dictionary<string, Dictionary<string, string>> _user;
        internal static string UserFile => Path.Combine(BepInEx.Paths.ConfigPath, "HideAndSeekMonster.animations.cfg");

        internal class Profile
        {
            public readonly Dictionary<string, string> Specs = new Dictionary<string, string>();
            public bool Flip, Hidden, NoClips, LockRoot;
            public readonly List<string> Proc = new List<string>();     // procedural rules (ProcAnim)
            public readonly List<string> HideParts = new List<string>(); // parts only visible while attacking
            public readonly List<string> KeepScale = new List<string>(); // parts that always keep their base size
            public readonly List<string> Smooth = new List<string>();    // parts whose height is smoothed (no bouncing)
            public float Bob;
            public float ActTime = 0.5f; // seconds of a procedural attack (act / chomp)
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
                else if (k == "noclips") p.NoClips = v != "0" && v != "false";
                else if (k == "lockroot") p.LockRoot = v != "0" && v != "false";
                else if (k == "proc") p.Proc.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                else if (k == "smooth") p.Smooth.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                else if (k == "keepscale") p.KeepScale.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                else if (k == "hidepart") p.HideParts.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                else if (k == "bob") float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.Bob);
                else if (k == "acttime") float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.ActTime);
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
            var words = q.Split(' ').Select(w => Regex.Replace(w, "[^a-z0-9!]", "")).Where(w => w.Length > 0 && w != "!").ToArray();
            var need = words.Where(w => w[0] != '!').ToArray();
            var not = words.Where(w => w[0] == '!' && w.Length > 1).Select(w => w.Substring(1)).ToArray();
            if (need.Length == 0) return null;
            // whole words only ("walk" must not match "Slow Walker"); long words may also hit a joined name
            bool Hit(string name, string w)
            {
                string n = name.ToLowerInvariant();
                if (Regex.Split(n, "[^a-z0-9]+").Contains(w)) return true;
                return w.Length >= 6 && Regex.Replace(n, "[^a-z0-9]", "").Contains(w);
            }
            return clips.Where(c => need.All(w => Hit(c.name, w)) && !not.Any(w => Hit(c.name, w)))
                .OrderBy(c => c.name.Length).FirstOrDefault();
        }
    }

    /// <summary>
    /// Plays a monster's animation clips directly (AnimationClip.SampleAnimation, the game's animator is switched
    /// off): idle / walk / run / jump / fall, attack, special, charge loops, Rugrat carrying.
    /// Every frame the model first goes back to its spawned, visible base pose and the clip is sampled on top, so
    /// whatever a clip doesn't animate stays visible (the enemy prefabs start hidden / scaled to 0 before spawning).
    /// </summary>
    internal class ClipAnimator
    {
        private readonly GameObject _root;
        private readonly Animator _anim;
        public readonly List<AnimationClip> Clips;
        private readonly AnimProfiles.Profile _profile;
        private readonly Dictionary<string, AnimProfiles.Track> _tracks = new Dictionary<string, AnimProfiles.Track>();
        private readonly string _monster;

        // base pose
        private Transform[] _bones;
        private Vector3[] _bPos, _bScale;
        private Quaternion[] _bRot;
        private bool[] _bActive;
        private Renderer[] _rends;
        private bool[] _rOn;

        // cross-fade
        private Vector3[] _fPos, _fScale;
        private Quaternion[] _fRot;
        private float _fade = 1f;
        private AnimationClip _lastClip;
        private float _lastTime;

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
        private float _previewUntil;

        public bool Charging, Carrying;
        private bool _wasCarrying;
        public bool ActionPlaying => _action != null;

        private ClipAnimator(Animator anim, List<AnimationClip> clips, AnimProfiles.Profile profile, string monster)
        {
            _anim = anim;
            _root = anim.gameObject;
            Clips = clips;
            _profile = profile;
            _monster = monster;
            Rebuild();
        }

        /// <summary>Takes over the animator, or returns null when the monster's clips don't fit (old behaviour stays).</summary>
        public static ClipAnimator TryCreate(Animator anim, string monster, AnimProfiles.Profile profile)
        {
            if (anim == null || anim.runtimeAnimatorController == null || profile.NoClips) return null;
            try
            {
                var clips = anim.runtimeAnimatorController.animationClips.Where(c => c != null && !c.legacy && c.length > 0.01f)
                    .GroupBy(c => c.name).Select(g => g.First()).ToList();
                if (clips.Count == 0) return null;
                var idle = AnimProfiles.Parse("idle", profile.Specs.TryGetValue("idle", out var s1) ? s1 : null, clips);
                var walk = AnimProfiles.Parse("walk", profile.Specs.TryGetValue("walk", out var s2) ? s2 : null, clips);
                if (idle == null && walk == null) { Plugin.Log.LogInfo($"[Anim] '{monster}': no idle/walk clip – keeping the game's animator"); return null; }
                var ca = new ClipAnimator(anim, clips, profile, monster);
                ca.TakeOver();
                return ca;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[Anim] '{monster}': clip player failed ({ex.Message}) – keeping the game's animator");
                if (anim != null) anim.enabled = true;
                return null;
            }
        }

        private void TakeOver()
        {
            _anim.enabled = false; // from now on only the sampled clips move the model
            // finish the spawn animation, so the base pose is the visible monster
            // init: clips sampled at their end one after another (Elsa: spawn, then grow big); then the first idle frame,
            // so whatever the spawn moved (Gambit's lift) is back where the monster normally stands
            AnimationClip spawn = null;
            string initSpec = _profile.Specs.TryGetValue("init", out var init) ? init : "spawn !despawn";
            // "a > b | c > d": several ways to start – the one that makes the model tallest wins (Elsa's big form)
            var options = initSpec.Split('|');
            string best = options[0];
            if (options.Length > 1)
            {
                float bestH = -1f;
                var ts = _root.GetComponentsInChildren<Transform>(true);
                var sp = ts.Select(t => t.localPosition).ToArray(); var sr = ts.Select(t => t.localRotation).ToArray();
                var ss = ts.Select(t => t.localScale).ToArray(); var sa = ts.Select(t => t.gameObject.activeSelf).ToArray();
                var rs0 = _root.GetComponentsInChildren<Renderer>(true); var re0 = rs0.Select(r => r.enabled).ToArray();
                void Reset()
                {
                    for (int i = 0; i < ts.Length; i++) { ts[i].localPosition = sp[i]; ts[i].localRotation = sr[i]; ts[i].localScale = ss[i]; if (ts[i].gameObject.activeSelf != sa[i]) ts[i].gameObject.SetActive(sa[i]); }
                    for (int i = 0; i < rs0.Length; i++) rs0[i].enabled = re0[i];
                }
                foreach (var opt in options)
                {
                    Reset();
                    foreach (var part in opt.Split('>')) { var c = AnimProfiles.FindClip(part, Clips); if (c != null) c.SampleAnimation(_root, c.length); }
                    float h = Height();
                    Plugin.Log.LogInfo($"[Anim] '{_monster}' start '{opt.Trim()}': {h:0.0} m");
                    if (h > bestH) { bestH = h; best = opt; }
                }
                Reset();
            }
            foreach (var part in best.Split('>'))
            {
                var c = AnimProfiles.FindClip(part, Clips);
                if (c == null) continue;
                c.SampleAnimation(_root, c.length);
                spawn = c;
            }
            var idleTrack = AnimProfiles.Parse("idle", _profile.Specs.TryGetValue("idle", out var idleSpec) ? idleSpec : null, Clips);
            if (idleTrack != null) { var st0 = idleTrack.Steps[0]; st0.Clip.SampleAnimation(_root, st0.A * st0.Clip.length); }
            _rends = _root.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (!_rends.Any(r => r.enabled && r.gameObject.activeInHierarchy))
                foreach (var r in _rends) { r.enabled = true; r.gameObject.SetActive(true); }
            _bones = _root.GetComponentsInChildren<Transform>(true);
            int n = _bones.Length;
            _bPos = new Vector3[n]; _bScale = new Vector3[n]; _bRot = new Quaternion[n]; _bActive = new bool[n];
            _fPos = new Vector3[n]; _fScale = new Vector3[n]; _fRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var b = _bones[i];
                _bPos[i] = b.localPosition; _bRot[i] = b.localRotation; _bScale[i] = b.localScale; _bActive[i] = b.gameObject.activeSelf;
            }
            _rOn = _rends.Select(r => r.enabled).ToArray();
            var keep = new List<int>();
            for (int i = 0; i < n; i++)
            {
                string bn = _bones[i].name.Trim().ToLowerInvariant();
                foreach (var k in _profile.KeepScale)
                {
                    string kk = k.Trim('*').Trim().ToLowerInvariant();
                    bool hit = k.StartsWith("*") || k.EndsWith("*") ? bn.Contains(kk) : bn == kk;
                    if (hit) { keep.Add(i); break; }
                }
            }
            _keepScale = keep.ToArray();
            var sm = new List<int>();
            for (int i = 0; i < n; i++)
                if (_profile.Smooth.Any(k => _bones[i].name.Trim().Equals(k.Trim(), System.StringComparison.OrdinalIgnoreCase))) sm.Add(i);
            _smooth = sm.ToArray();
            _smoothY = _smooth.Select(i => _bPos[i].y).ToArray();
            if (_keepScale.Length > 0) Plugin.Log.LogInfo($"[Anim] '{_monster}': {_keepScale.Length} part(s) keep their size");
            Plugin.Log.LogInfo($"[Anim] '{_monster}': base pose from '{(spawn != null ? spawn.name : "current")}', {n} bones, {_rOn.Count(x => x)}/{_rends.Length} renderers on");
        }

        private readonly HashSet<Renderer> _keepOff = new HashSet<Renderer>();
        private int[] _keepScale = new int[0];
        private int[] _smooth = new int[0];
        private float[] _smoothY = new float[0];
        private bool _walking;
        public void KeepOff(IEnumerable<Renderer> rs)
        {
            foreach (var r in rs) if (r != null) _keepOff.Add(r);
            for (int i = 0; i < _rends.Length; i++) if (_keepOff.Contains(_rends[i])) _rOn[i] = false;
        }

        private void RestoreBase()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = _bones[i];
                if (b == null) continue;
                b.localPosition = _bPos[i]; b.localRotation = _bRot[i]; b.localScale = _bScale[i];
                if (b.gameObject.activeSelf != _bActive[i]) b.gameObject.SetActive(_bActive[i]);
            }
            for (int i = 0; i < _rends.Length; i++) if (_rends[i] != null) _rends[i].enabled = _rOn[i];
        }

        private float Height()
        {
            var rs = _root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer) && r.bounds.size.magnitude < 15f).ToArray();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        /// <summary>Gives the game's animator back (for copies of the body).</summary>
        public void Release() { if (_anim != null) _anim.enabled = true; }

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
            _previewUntil = Time.time + 6f;
            if (t != null) Start(t);
        }

        private void Start(AnimProfiles.Track t)
        {
            if (_cur != null && _cur != t) _fade = 0f;
            _cur = t;
            _step = 0;
            _t = 0f;
            _finished = false;
        }

        public void Tick(float dt, float speed, bool grounded, float vy)
        {
            if (_root == null) return;

            if (Carrying != _wasCarrying)
            {
                _wasCarrying = Carrying;
                PlayAction(Carrying ? "pickup" : "throw");
            }

            if (_preview != null && (_cur != _preview || _finished || Time.time > _previewUntil)) _preview = null;
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
                _walking = _walking ? speed > 0.15f : speed > 0.6f;
                if (want == null && _walking) { want = TrackOf(c + "walk") ?? TrackOf("walk") ?? TrackOf("run"); rate = Mathf.Clamp(speed / 3f, 0.6f, 1.8f); }
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
            float time = (st.A + (st.B - st.A) * frac) * st.Clip.length;

            // short cross-fade of the bones from the previous clip
            bool fading = _fade < 1f && _lastClip != null;
            if (fading)
            {
                RestoreBase();
                _lastClip.SampleAnimation(_root, _lastTime);
                for (int i = 0; i < _bones.Length; i++)
                {
                    var b = _bones[i];
                    if (b == null) continue;
                    _fPos[i] = b.localPosition; _fRot[i] = b.localRotation; _fScale[i] = b.localScale;
                }
                _fade = Mathf.Min(1f, _fade + dt / 0.15f);
            }
            RestoreBase();
            st.Clip.SampleAnimation(_root, time);
            foreach (var r in _keepOff) if (r != null) r.enabled = false;
            foreach (int i in _keepScale) if (_bones[i] != null) _bones[i].localScale = _bScale[i];
            if (fading)
            {
                float f = Mathf.SmoothStep(0f, 1f, _fade);
                for (int i = 0; i < _bones.Length; i++)
                {
                    var b = _bones[i];
                    if (b == null) continue;
                    b.localPosition = Vector3.LerpUnclamped(_fPos[i], b.localPosition, f);
                    b.localRotation = Quaternion.SlerpUnclamped(_fRot[i], b.localRotation, f);
                    b.localScale = Vector3.LerpUnclamped(_fScale[i], b.localScale, f);
                }
            }
            else
            {
                _lastClip = st.Clip;
                _lastTime = time;
            }
            if (fading && _fade >= 1f) { _lastClip = st.Clip; _lastTime = time; }
            foreach (int i in _keepScale) if (_bones[i] != null) _bones[i].localScale = _bScale[i]; // also after the cross-fade
            if (_profile.LockRoot) // clips must not lift / move the whole model (Gambit's trap animation)
                for (int i = 0; i < _bones.Length; i++)
                    if (_bones[i] != null && _bones[i].parent == _root.transform) _bones[i].localPosition = _bPos[i];
            // smoothed height: the body doesn't bounce with every step or when switching between standing and walking
            float k = 1f - Mathf.Exp(-dt / 0.35f);
            for (int j = 0; j < _smooth.Length; j++)
            {
                var b = _bones[_smooth[j]];
                if (b == null) continue;
                var lp = b.localPosition;
                _smoothY[j] = dt <= 0f ? lp.y : Mathf.Lerp(_smoothY[j], lp.y, k);
                b.localPosition = new Vector3(lp.x, _smoothY[j], lp.z);
            }
        }

        private static bool IsLoco(AnimProfiles.Track t) =>
            t.Role == "idle" || t.Role == "walk" || t.Role == "run" || t.Role == "c-idle" || t.Role == "c-walk";

        public void Destroy() { }
    }

    /// <summary>
    /// Procedural animation for monsters the game animates by code (no clips): rotates / moves / scales named parts
    /// while walking, idling, attacking or using the special. Rule: "kind part name:axis:amount[:phase]",
    /// part "@root" = the whole model, a trailing '*' = name contains.
    /// Kinds: spin (deg per metre), swing (walk cycle), idle, hover (move), bounce (move while walking), breathe (scale),
    /// hunch (while moving), act (once per attack), chomp (bites during attack / special), spec (held during the special).
    /// </summary>
    internal class ProcAnim
    {
        private class Rule { public string Kind; public List<Transform> Parts; public int Axis; public bool ModelSpace; public float Amount, Phase; }
        private class State { public Quaternion RawRot, SetRot; public Vector3 RawPos, SetPos, RawScale, SetScale; public bool Init; }

        private readonly List<Rule> _rules = new List<Rule>();
        private readonly Dictionary<Transform, State> _state = new Dictionary<Transform, State>();
        private float _dist, _walkPhase;
        private Transform _root;

        public bool Empty => _rules.Count == 0;

        public static ProcAnim Create(Transform root, IEnumerable<string> specs, string monster)
        {
            var pa = new ProcAnim { _root = root };
            var all = root.GetComponentsInChildren<Transform>(true);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var found = new List<string>();
            foreach (var spec in specs)
            {
                int sp = spec.IndexOf(' ');
                if (sp < 0) continue;
                var parts = spec.Substring(sp + 1).Split(':');
                if (parts.Length < 2) continue;
                var rule = new Rule { Kind = spec.Substring(0, sp).Trim().ToLowerInvariant(), Parts = Find(root, all, parts[0].Trim()) };
                // lower-case axis = the part's own axis, upper-case X/Y/Z = the monster's axes (X right, Y up, Z forward)
                string axRaw = parts.Length > 1 ? parts[1].Trim() : "x";
                rule.ModelSpace = axRaw.Length > 0 && char.IsUpper(axRaw[0]);
                string ax = axRaw.ToLowerInvariant();
                rule.Axis = ax == "y" ? 1 : ax == "z" ? 2 : 0;
                if (parts.Length > 2) float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, inv, out rule.Amount);
                if (parts.Length > 3) float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, inv, out rule.Phase);
                if (rule.Parts.Count == 0) { found.Add(parts[0].Trim() + " (not found)"); continue; }
                found.Add($"{rule.Kind} {parts[0].Trim()} x{rule.Parts.Count}");
                pa._rules.Add(rule);
            }
            Plugin.Log.LogInfo($"[Anim] '{monster}' procedural: " + string.Join(", ", found));
            return pa;
        }

        private static List<Transform> Find(Transform root, Transform[] all, string name)
        {
            if (name == "@root") return new List<Transform> { root };
            bool contains = name.EndsWith("*");
            string n = name.TrimEnd('*').Trim().ToLowerInvariant();
            return all.Where(t => t != root && (contains ? t.name.ToLowerInvariant().Contains(n) : t.name.Trim().ToLowerInvariant() == n)).ToList();
        }

        private static Vector3 Axis(int a) => a == 1 ? Vector3.up : a == 2 ? Vector3.forward : Vector3.right;

        /// <param name="attack">0..1 progress of the current attack, -1 = none</param>
        /// <param name="special">0..1 strength of the special (ramps in / out), 0 = none</param>
        public void Tick(float dt, float speed, float attack, float special, float specialTime)
        {
            float walk = Mathf.Clamp01(speed / 2.5f);
            _dist += speed * dt;
            _walkPhase += speed * dt * 3.2f;
            float t = Time.time;

            // what each part gets this frame
            var rot = new Dictionary<Transform, Quaternion>();
            var pos = new Dictionary<Transform, Vector3>();
            var scl = new Dictionary<Transform, float>();
            var wrot = new Dictionary<Transform, Quaternion>(); // rotations around the monster's axes
            foreach (var r in _rules)
            {
                float v = 0f;
                bool move = false, scale = false;
                switch (r.Kind)
                {
                    case "spin": v = _dist * r.Amount; break;
                    case "swing": v = Mathf.Sin(_walkPhase + r.Phase * Mathf.Deg2Rad) * r.Amount * walk; break;
                    case "idle": v = Mathf.Sin(t * 2f + r.Phase) * r.Amount; break;
                    case "hunch": v = r.Amount * walk; break;
                    case "act": v = attack >= 0f ? Mathf.Sin(attack * Mathf.PI) * r.Amount : 0f; break;
                    case "chomp":
                        v = attack >= 0f ? Mathf.Abs(Mathf.Sin(attack * Mathf.PI * 2f)) * r.Amount
                          : special > 0f ? Mathf.Abs(Mathf.Sin(specialTime * Mathf.PI * 2.5f)) * r.Amount * special : 0f;
                        break;
                    case "spec": v = special * r.Amount; break;
                    case "hover": v = Mathf.Sin(t * 1.6f + r.Phase) * r.Amount; move = true; break;
                    case "bounce": v = Mathf.Abs(Mathf.Sin(_walkPhase)) * r.Amount * walk; move = true; break;
                    case "breathe": v = Mathf.Sin(t * 2f + r.Phase) * r.Amount; scale = true; break;
                }
                foreach (var p in r.Parts)
                {
                    if (p == null) continue;
                    if (move) pos[p] = (pos.TryGetValue(p, out var o) ? o : Vector3.zero) + Axis(r.Axis) * v;
                    else if (scale) scl[p] = (scl.TryGetValue(p, out var s0) ? s0 : 1f) * (1f + v);
                    else if (r.ModelSpace)
                        wrot[p] = Quaternion.AngleAxis(v, _root.rotation * Axis(r.Axis)) * (wrot.TryGetValue(p, out var w) ? w : Quaternion.identity);
                    else rot[p] = (rot.TryGetValue(p, out var q) ? q : Quaternion.identity) * Quaternion.AngleAxis(v, Axis(r.Axis));
                }
            }

            foreach (var r in _rules)
                foreach (var p in r.Parts)
                {
                    if (p == null) continue;
                    if (!_state.TryGetValue(p, out var st)) _state[p] = st = new State();
                    // the game's animator may have written a fresh pose: then that is the new raw pose
                    if (!st.Init || p.localRotation != st.SetRot) st.RawRot = p.localRotation;
                    if (!st.Init || p.localPosition != st.SetPos) st.RawPos = p.localPosition;
                    if (!st.Init || p.localScale != st.SetScale) st.RawScale = p.localScale;
                    st.Init = true;
                    var local = st.RawRot * (rot.TryGetValue(p, out var q) ? q : Quaternion.identity);
                    if (wrot.TryGetValue(p, out var wq))
                    {
                        // turn the part in world space around the monster's axis: local' = inv(parent) * R * parent * local
                        var parent = p.parent != null ? p.parent.rotation : Quaternion.identity;
                        local = Quaternion.Inverse(parent) * wq * parent * local;
                    }
                    p.localRotation = st.SetRot = local;
                    p.localPosition = st.SetPos = st.RawPos + (pos.TryGetValue(p, out var o) ? o : Vector3.zero);
                    p.localScale = st.SetScale = st.RawScale * (scl.TryGetValue(p, out var s1) ? s1 : 1f);
                }
        }
    }
}
