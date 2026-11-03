using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// The monster body of the seeker. Clones the visuals of a real R.E.P.O. enemy prefab
    /// (all scripts, physics and network components stripped) and makes it follow the seeker.
    /// Falls back to a self-built monster if the clone fails.
    /// Pure client-side cosmetics: every client builds it locally.
    /// </summary>
    internal class MonsterVisual : MonoBehaviour
    {
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private PlayerAvatar _avatar;
        private bool _isLocal;
        private Transform _body;
        private Animator _animator;
        private AudioSource _audio;
        private MonsterCatalog.Entry _entry;
        private Vector3 _lastPos;
        private float _refreshTimer;
        private float _lungeTimer;
        private float _yaw;

        // fallback parts
        private Transform _jaw, _armL, _armR;
        private Light _light;
        private static Shader _unlit;

        public string MonsterName => _entry != null ? _entry.Name : "Monster";
        public bool IsFallback { get; private set; }
        public float BodyHeight { get; private set; } = 2f;
        public string LastSoundInfo { get; private set; } = "-";
        public string DebugInfo { get; private set; } = "";
        private int _stripped, _stripFailed;
        private string _subtree = "-";

        public static MonsterVisual Create(PlayerAvatar avatar, string monsterName, float scale)
        {
            if (avatar == null) return null;
            var go = new GameObject("HNS_Monster");
            var mv = go.AddComponent<MonsterVisual>();
            mv.Init(avatar, monsterName, scale);
            return mv;
        }

        private void Init(PlayerAvatar avatar, string monsterName, float scale)
        {
            _avatar = avatar;
            _isLocal = GameRefs.IsLocal(avatar);
            _entry = MonsterCatalog.Find(monsterName);

            var target = FollowTarget();
            if (target != null) { transform.position = target.position; _yaw = target.eulerAngles.y; }
            _lastPos = transform.position;

            if (_entry != null)
            {
                try { _body = CloneEnemyVisual(_entry.Prefab); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Could not clone '{_entry.Name}': {ex.Message}"); }
            }
            else if (!string.IsNullOrEmpty(monsterName))
            {
                Plugin.Log.LogWarning($"Monster '{monsterName}' not in catalog – using custom monster.");
            }
            if (_body == null) { IsFallback = true; BuildFallback(); }

            _body.localScale *= scale;
            SetLayerRecursive(gameObject, 0);
            Vector3 size = GroundBody();
            if (size.y > 0.2f) BodyHeight = size.y;

            int rendCount = _body.GetComponentsInChildren<Renderer>(true).Length;
            string animInfo = "none";
            if (_animator != null)
            {
                var ctrl = _animator.runtimeAnimatorController;
                animInfo = ctrl == null ? "no controller"
                    : $"{ctrl.name} [{string.Join(", ", _animator.parameters.Select(p => p.name + ":" + p.type))}]";
            }
            DebugInfo =
                $"Model: {(IsFallback ? "CUSTOM (fallback)" : "game enemy")}\n" +
                $"Subtree: {_subtree}\n" +
                $"Renderers: {rendCount}   Size: {size.x:0.0} x {size.y:0.0} x {size.z:0.0} m\n" +
                $"Stripped components: {_stripped} (failed: {_stripFailed})\n" +
                $"Sounds: roar {_entry?.RoarClips.Count ?? 0}, attack {_entry?.AttackClips.Count ?? 0}\n" +
                $"Animator: {animInfo}";
            Plugin.Log.LogInfo($"[Monster] '{MonsterName}' built for {(_isLocal ? "local" : "remote")} player\n{DebugInfo}");

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 3f;
            _audio.maxDistance = 40f;
            _audio.volume = 1f;
            _audio.playOnAwake = false;

            HideOriginal();
        }

        // ------------------------------------------------------------------
        // Enemy clone
        // ------------------------------------------------------------------
        private Transform CloneEnemyVisual(GameObject prefab)
        {
            var holder = new GameObject("HNS_Holder");
            holder.SetActive(false); // nothing on the clone will Awake while we strip it
            var clone = Instantiate(prefab, holder.transform);

            StripComponents(clone);

            // Pick the subtree that actually looks like the monster: the animator with the most renderers.
            Transform best = null;
            int bestCount = 0;
            foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
            {
                int n = anim.GetComponentsInChildren<Renderer>(true).Length;
                if (n > bestCount) { bestCount = n; best = anim.transform; }
            }
            if (best == null) best = clone.transform;
            _subtree = best.name + $" ({bestCount} renderers under animator)";
            if (best.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                Plugin.Log.LogWarning($"[Monster] '{prefab.name}' has no renderers after stripping – fallback.");
                Destroy(holder);
                return null;
            }

            var body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            best.SetParent(body, false);
            best.localPosition = Vector3.zero;
            best.localRotation = Quaternion.identity;
            best.gameObject.SetActive(true);
            Destroy(holder);

            _animator = best.GetComponent<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.enabled = true;
            }
            var rends = body.GetComponentsInChildren<Renderer>(true);
            bool anyOn = rends.Any(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer));
            foreach (var r in rends)
            {
                if (!anyOn && !(r is ParticleSystemRenderer)) { r.enabled = true; r.gameObject.SetActive(true); }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            return body;
        }

        /// <summary>
        /// Removes every non-visual component. Only removes a component once nothing left on its
        /// GameObject still requires it ([RequireComponent]), so Unity never complains.
        /// </summary>
        private void StripComponents(GameObject clone)
        {
            var remaining = clone.GetComponentsInChildren<Component>(true).Where(c => c != null && !Keep(c)).ToList();
            for (int pass = 0; pass < 12 && remaining.Count > 0; pass++)
            {
                bool progress = false;
                foreach (var c in remaining.ToArray())
                {
                    if (c == null) { remaining.Remove(c); continue; }
                    if (IsRequiredByOther(c)) continue;
                    DestroyImmediate(c);
                    remaining.Remove(c);
                    _stripped++;
                    progress = true;
                }
                if (!progress) break;
            }
            remaining.RemoveAll(c => c == null);
            _stripFailed = remaining.Count;
            // Whatever is still left can't run anyway: disable it.
            foreach (var c in remaining)
                if (c is Behaviour b) b.enabled = false;
        }

        private static readonly Dictionary<System.Type, System.Type[]> RequiresCache = new Dictionary<System.Type, System.Type[]>();

        private static System.Type[] Requires(System.Type t)
        {
            if (RequiresCache.TryGetValue(t, out var r)) return r;
            var list = new List<System.Type>();
            foreach (RequireComponent rc in t.GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (rc.m_Type0 != null) list.Add(rc.m_Type0);
                if (rc.m_Type1 != null) list.Add(rc.m_Type1);
                if (rc.m_Type2 != null) list.Add(rc.m_Type2);
            }
            r = list.ToArray();
            RequiresCache[t] = r;
            return r;
        }

        private static bool IsRequiredByOther(Component c)
        {
            var ct = c.GetType();
            foreach (var other in c.GetComponents<Component>())
            {
                if (other == null || other == c) continue;
                foreach (var req in Requires(other.GetType()))
                    if (req.IsAssignableFrom(ct))
                    {
                        // Only blocks if c is the last component of that type on the object.
                        if (c.GetComponents(req).Length <= 1) return true;
                    }
            }
            return false;
        }

        private static bool Keep(Component c) =>
            c is Transform || (c is Renderer && !(c is ParticleSystemRenderer)) || c is MeshFilter || c is Animator ||
            c is Light || c is LODGroup;

        private Vector3 GroundBody()
        {
            var renderers = _body.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) return Vector3.zero;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float feet = b.min.y - transform.position.y;
            _body.position -= new Vector3(0, feet, 0);
            return b.size;
        }

        // ------------------------------------------------------------------
        // Fallback monster (primitives)
        // ------------------------------------------------------------------
        private void BuildFallback()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            var skin = new Color(0.07f, 0.03f, 0.04f);
            var flesh = new Color(0.35f, 0.04f, 0.05f);
            var bone = new Color(0.85f, 0.82f, 0.7f);
            var glow = new Color(1f, 0.1f, 0.05f);

            Part(PrimitiveType.Capsule, _body, new Vector3(0, 1.15f, 0), new Vector3(0.95f, 0.75f, 0.7f), Vector3.zero, skin);
            Part(PrimitiveType.Sphere, _body, new Vector3(0, 1.35f, 0.18f), new Vector3(0.7f, 0.55f, 0.45f), Vector3.zero, flesh);
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, _body, new Vector3(0, 1.75f - i * 0.22f, -0.33f - i * 0.02f), new Vector3(0.06f, 0.22f, 0.06f), new Vector3(-35, 45, 0), bone);

            var head = new GameObject("Head").transform;
            head.SetParent(_body, false);
            head.localPosition = new Vector3(0, 2.0f, 0.15f);
            Part(PrimitiveType.Sphere, head, Vector3.zero, new Vector3(0.62f, 0.5f, 0.6f), Vector3.zero, skin);
            Part(PrimitiveType.Cylinder, head, new Vector3(-0.22f, 0.3f, -0.05f), new Vector3(0.07f, 0.22f, 0.07f), new Vector3(-20, 0, 30), bone);
            Part(PrimitiveType.Cylinder, head, new Vector3(0.22f, 0.3f, -0.05f), new Vector3(0.07f, 0.22f, 0.07f), new Vector3(-20, 0, -30), bone);
            Part(PrimitiveType.Sphere, head, new Vector3(-0.13f, 0.06f, 0.27f), Vector3.one * 0.11f, Vector3.zero, glow, true);
            Part(PrimitiveType.Sphere, head, new Vector3(0.13f, 0.06f, 0.27f), Vector3.one * 0.11f, Vector3.zero, glow, true);
            _jaw = new GameObject("Jaw").transform;
            _jaw.SetParent(head, false);
            _jaw.localPosition = new Vector3(0, -0.15f, 0.12f);
            Part(PrimitiveType.Cube, _jaw, new Vector3(0, -0.04f, 0.1f), new Vector3(0.42f, 0.08f, 0.3f), Vector3.zero, flesh);
            for (int i = 0; i < 5; i++)
                Part(PrimitiveType.Cube, _jaw, new Vector3(-0.16f + i * 0.08f, 0.02f, 0.23f), new Vector3(0.03f, 0.08f, 0.03f), Vector3.zero, bone);
            _armL = Arm(_body, -1, skin, bone);
            _armR = Arm(_body, 1, skin, bone);
            Part(PrimitiveType.Capsule, _body, new Vector3(-0.25f, 0.4f, 0), new Vector3(0.28f, 0.42f, 0.28f), Vector3.zero, skin);
            Part(PrimitiveType.Capsule, _body, new Vector3(0.25f, 0.4f, 0), new Vector3(0.28f, 0.42f, 0.28f), Vector3.zero, skin);

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(head, false);
            lightGo.transform.localPosition = new Vector3(0, 0, 0.6f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = glow;
            _light.range = 4f;
            _light.intensity = 2.5f;
            _light.shadows = LightShadows.None;
        }

        private static Transform Arm(Transform parent, int side, Color skin, Color bone)
        {
            var shoulder = new GameObject(side < 0 ? "ArmL" : "ArmR").transform;
            shoulder.SetParent(parent, false);
            shoulder.localPosition = new Vector3(0.55f * side, 1.6f, 0.05f);
            Part(PrimitiveType.Capsule, shoulder, new Vector3(0.05f * side, -0.45f, 0.1f), new Vector3(0.2f, 0.45f, 0.2f), new Vector3(-15, 0, 10 * side), skin);
            for (int i = -1; i <= 1; i++)
                Part(PrimitiveType.Cube, shoulder, new Vector3(0.08f * side + i * 0.06f, -0.95f, 0.2f), new Vector3(0.03f, 0.2f, 0.03f), new Vector3(-30, 0, 0), bone);
            return shoulder;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Color color, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (emissive)
            {
                if (_unlit == null) _unlit = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
                if (_unlit != null) r.material = new Material(_unlit);
            }
            r.material.color = color;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ------------------------------------------------------------------
        // Runtime
        // ------------------------------------------------------------------
        private Transform FollowTarget()
        {
            if (_avatar == null) return null;
            if (!_isLocal && _avatar.playerAvatarVisuals != null) return _avatar.playerAvatarVisuals.transform;
            return _avatar.transform;
        }

        private void HideOriginal()
        {
            if (_isLocal || _avatar == null || _avatar.playerAvatarVisuals == null) return;
            var meshParent = _avatar.playerAvatarVisuals.meshParent;
            var root = meshParent != null ? meshParent.transform : _avatar.playerAvatarVisuals.transform;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                if (!_hiddenRenderers.Contains(r)) _hiddenRenderers.Add(r);
            }
        }

        private void LateUpdate()
        {
            var target = FollowTarget();
            if (target == null) { Destroy(gameObject); return; }

            transform.position = target.position;
            // Local seeker: face where the camera looks. Remote: face where the avatar faces.
            float wantYaw = target.eulerAngles.y;
            var view = _isLocal ? GameRefs.ViewTransform(null) : null;
            if (view != null)
            {
                var f = view.forward; f.y = 0;
                if (f.sqrMagnitude > 0.001f) wantYaw = Quaternion.LookRotation(f).eulerAngles.y;
            }
            _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.Euler(0, _yaw, 0);

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f) { _refreshTimer = 0.5f; HideOriginal(); }

            float speed = (transform.position - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastPos = transform.position;
            Animate(speed);
        }

        private void Animate(float speed)
        {
            float t = Time.time;
            if (_lungeTimer > 0f) _lungeTimer -= Time.deltaTime;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    string n = p.name.ToLowerInvariant();
                    if (p.type == AnimatorControllerParameterType.Bool)
                    {
                        if (n.Contains("walk") || n.Contains("move")) _animator.SetBool(p.nameHash, speed > 0.3f);
                        else if (n.Contains("run") || n.Contains("sprint") || n.Contains("chase")) _animator.SetBool(p.nameHash, speed > 3.5f);
                        else if (n.Contains("attack")) _animator.SetBool(p.nameHash, _lungeTimer > 0f);
                    }
                    else if (p.type == AnimatorControllerParameterType.Float && (n.Contains("speed") || n.Contains("velocity")))
                    {
                        _animator.SetFloat(p.nameHash, speed);
                    }
                }
            }

            // Fallback monster animation
            float swing = Mathf.Clamp01(speed / 4f) * 35f;
            float lunge = _lungeTimer > 0f ? -70f : 0f;
            if (_jaw != null) _jaw.localEulerAngles = new Vector3((_lungeTimer > 0 ? 35f : 10f) + Mathf.Sin(t * 3f) * 10f, 0, 0);
            if (_armL != null) _armL.localEulerAngles = new Vector3(Mathf.Sin(t * 8f) * swing - 20f + lunge, 0, 0);
            if (_armR != null) _armR.localEulerAngles = new Vector3(-Mathf.Sin(t * 8f) * swing - 20f + lunge, 0, 0);
            if (_light != null) _light.intensity = 2.2f + Mathf.PerlinNoise(t * 4f, 0f) * 1.2f;
        }

        public void PlayAttack()
        {
            _lungeTimer = 0.45f;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger && p.name.ToLowerInvariant().Contains("attack"))
                        _animator.SetTrigger(p.nameHash);
            }
            PlaySound(_entry?.AttackSounds, _entry?.AttackClips, 1f);
        }

        public void PlayRoar()
        {
            PlaySound(_entry?.RoarSounds, _entry?.RoarClips, 1.3f);
        }

        /// <summary>
        /// Plays through the game's own Sound system first (correct mixer/volume),
        /// falls back to a plain AudioSource, and finally to a generated growl.
        /// </summary>
        private void PlaySound(List<Sound> sounds, List<AudioClip> clips, float volume)
        {
            Vector3 pos = transform.position + Vector3.up * Mathf.Min(BodyHeight, 2.5f) * 0.7f;
            if (sounds != null && sounds.Count > 0)
            {
                var snd = sounds[Random.Range(0, sounds.Count)];
                try
                {
                    var src = snd.Play(pos, volume, 1f, 1f, 1f);
                    if (src != null) { LastSoundInfo = "game sound: " + (src.clip != null ? src.clip.name : "?"); return; }
                    LastSoundInfo = "game sound returned no source";
                }
                catch (System.Exception ex) { LastSoundInfo = "game sound failed: " + ex.Message; }
            }

            AudioClip clip = null;
            if (clips != null && clips.Count > 0) clip = clips[Random.Range(0, clips.Count)];
            if (clip == null) { clip = Growl(); LastSoundInfo = "generated growl (monster has no sounds)"; }
            else LastSoundInfo = "audio source: " + clip.name;

            if (_audio == null) return;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            _audio.pitch = Random.Range(0.9f, 1.05f);
            _audio.PlayOneShot(clip, volume);
        }

        private static AudioClip _growl;

        /// <summary>A synthetic low growl, used when the monster has no usable sounds.</summary>
        private static AudioClip Growl()
        {
            if (_growl != null) return _growl;
            const int rate = 44100;
            int n = (int)(rate * 1.4f);
            var data = new float[n];
            var rnd = new System.Random(7);
            float phase = 0f, noise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((1.4f - t) * 2f);
                float freq = 70f + 25f * Mathf.Sin(t * 9f) + 15f * Mathf.Sin(t * 23f);
                phase += 2f * Mathf.PI * freq / rate;
                noise = noise * 0.97f + ((float)rnd.NextDouble() * 2f - 1f) * 0.03f;
                float saw = (phase / Mathf.PI % 2f) - 1f;
                data[i] = env * (0.55f * saw + 3f * noise) * 0.6f;
            }
            _growl = AudioClip.Create("HNS_Growl", n, 1, rate, false);
            _growl.SetData(data, 0);
            return _growl;
        }

        public void SetBodyVisible(bool visible)
        {
            if (_body != null && _body.gameObject.activeSelf != visible) _body.gameObject.SetActive(visible);
        }

        public void Remove() => Destroy(gameObject);

        private void OnDestroy()
        {
            foreach (var r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
        }
    }
}
