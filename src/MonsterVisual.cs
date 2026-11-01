using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Builds a monster out of primitives and attaches it to a player's visuals.
    /// Pure client-side cosmetics: every client builds it locally for the seeker.
    /// The original robot renderers are hidden while the monster is active.
    /// </summary>
    internal class MonsterVisual : MonoBehaviour
    {
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private PlayerAvatar _avatar;
        private Transform _root;
        private Transform _jaw;
        private Transform _armL, _armR;
        private Light _light;
        private float _refreshTimer;
        private Vector3 _lastPos;

        private static Shader _unlit;

        public static MonsterVisual Attach(PlayerAvatar avatar, float scale)
        {
            if (avatar == null || avatar.playerAvatarVisuals == null) return null;
            var host = avatar.playerAvatarVisuals.gameObject;
            var mv = host.GetComponent<MonsterVisual>();
            if (mv != null) Destroy(mv);
            mv = host.AddComponent<MonsterVisual>();
            mv.Build(avatar, scale);
            return mv;
        }

        private void Build(PlayerAvatar avatar, float scale)
        {
            _avatar = avatar;
            _lastPos = transform.position;

            var root = new GameObject("HNS_Monster");
            _root = root.transform;
            _root.SetParent(transform, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one * scale;

            var skin = new Color(0.07f, 0.03f, 0.04f);
            var flesh = new Color(0.35f, 0.04f, 0.05f);
            var bone = new Color(0.85f, 0.82f, 0.7f);
            var glow = new Color(1f, 0.1f, 0.05f);

            // Body
            Part(PrimitiveType.Capsule, _root, new Vector3(0, 1.15f, 0), new Vector3(0.95f, 0.75f, 0.7f), Vector3.zero, skin);
            Part(PrimitiveType.Sphere, _root, new Vector3(0, 1.35f, 0.18f), new Vector3(0.7f, 0.55f, 0.45f), Vector3.zero, flesh);
            // Spine spikes
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, _root, new Vector3(0, 1.75f - i * 0.22f, -0.33f - i * 0.02f),
                    new Vector3(0.06f, 0.22f, 0.06f), new Vector3(-35, 45, 0), bone);

            // Head
            var head = new GameObject("Head").transform;
            head.SetParent(_root, false);
            head.localPosition = new Vector3(0, 2.0f, 0.15f);
            Part(PrimitiveType.Sphere, head, Vector3.zero, new Vector3(0.62f, 0.5f, 0.6f), Vector3.zero, skin);
            // Horns
            Part(PrimitiveType.Cylinder, head, new Vector3(-0.22f, 0.3f, -0.05f), new Vector3(0.07f, 0.22f, 0.07f), new Vector3(-20, 0, 30), bone);
            Part(PrimitiveType.Cylinder, head, new Vector3(0.22f, 0.3f, -0.05f), new Vector3(0.07f, 0.22f, 0.07f), new Vector3(-20, 0, -30), bone);
            // Eyes (glowing)
            Part(PrimitiveType.Sphere, head, new Vector3(-0.13f, 0.06f, 0.27f), Vector3.one * 0.11f, Vector3.zero, glow, true);
            Part(PrimitiveType.Sphere, head, new Vector3(0.13f, 0.06f, 0.27f), Vector3.one * 0.11f, Vector3.zero, glow, true);
            // Jaw with teeth
            _jaw = new GameObject("Jaw").transform;
            _jaw.SetParent(head, false);
            _jaw.localPosition = new Vector3(0, -0.15f, 0.12f);
            Part(PrimitiveType.Cube, _jaw, new Vector3(0, -0.04f, 0.1f), new Vector3(0.42f, 0.08f, 0.3f), Vector3.zero, flesh);
            for (int i = 0; i < 5; i++)
                Part(PrimitiveType.Cube, _jaw, new Vector3(-0.16f + i * 0.08f, 0.02f, 0.23f), new Vector3(0.03f, 0.08f, 0.03f), Vector3.zero, bone);

            // Arms with claws
            _armL = Arm(_root, -1, skin, bone);
            _armR = Arm(_root, 1, skin, bone);

            // Legs
            Part(PrimitiveType.Capsule, _root, new Vector3(-0.25f, 0.4f, 0), new Vector3(0.28f, 0.42f, 0.28f), Vector3.zero, skin);
            Part(PrimitiveType.Capsule, _root, new Vector3(0.25f, 0.4f, 0), new Vector3(0.28f, 0.42f, 0.28f), Vector3.zero, skin);

            // Red glow
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(head, false);
            lightGo.transform.localPosition = new Vector3(0, 0, 0.6f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = glow;
            _light.range = 4f;
            _light.intensity = 2.5f;
            _light.shadows = LightShadows.None;

            HideOriginal();
        }

        private Transform Arm(Transform parent, int side, Color skin, Color bone)
        {
            var shoulder = new GameObject(side < 0 ? "ArmL" : "ArmR").transform;
            shoulder.SetParent(parent, false);
            shoulder.localPosition = new Vector3(0.55f * side, 1.6f, 0.05f);
            Part(PrimitiveType.Capsule, shoulder, new Vector3(0.05f * side, -0.45f, 0.1f), new Vector3(0.2f, 0.45f, 0.2f), new Vector3(-15, 0, 10 * side), skin);
            for (int i = -1; i <= 1; i++)
                Part(PrimitiveType.Cube, shoulder, new Vector3(0.08f * side + i * 0.06f, -0.95f, 0.2f), new Vector3(0.03f, 0.2f, 0.03f), new Vector3(-30, 0, 0), bone);
            return shoulder;
        }

        private static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Color color, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            go.layer = parent.gameObject.layer;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (emissive)
                {
                    if (_unlit == null) _unlit = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
                    if (_unlit != null) r.material = new Material(_unlit);
                }
                r.material.color = color;
            }
            return go;
        }

        private void HideOriginal()
        {
            if (_avatar == null || _avatar.playerAvatarVisuals == null) return;
            var meshParent = _avatar.playerAvatarVisuals.meshParent;
            var searchRoot = meshParent != null ? meshParent.transform : _avatar.playerAvatarVisuals.transform;
            foreach (var r in searchRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.transform.IsChildOf(_root)) continue;
                if (r.enabled)
                {
                    r.enabled = false;
                    if (!_hiddenRenderers.Contains(r)) _hiddenRenderers.Add(r);
                }
            }
        }

        private void Update()
        {
            if (_root == null) return;

            // The game may toggle renderers back on (cosmetics, damage flashes, ...).
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                HideOriginal();
            }

            // Simple procedural animation: breathing jaw, swinging arms while moving, flickering glow.
            float t = Time.time;
            float speed = (transform.position - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastPos = transform.position;
            float swing = Mathf.Clamp01(speed / 4f) * 35f;

            if (_jaw != null) _jaw.localEulerAngles = new Vector3(10f + Mathf.Sin(t * 3f) * 10f, 0, 0);
            if (_armL != null) _armL.localEulerAngles = new Vector3(Mathf.Sin(t * 8f) * swing - 20f, 0, 0);
            if (_armR != null) _armR.localEulerAngles = new Vector3(-Mathf.Sin(t * 8f) * swing - 20f, 0, 0);
            if (_light != null) _light.intensity = 2.2f + Mathf.PerlinNoise(t * 4f, 0f) * 1.2f;
        }

        public void Remove()
        {
            foreach (var r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
            if (_root != null) Destroy(_root.gameObject);
            Destroy(this);
        }

        private void OnDestroy()
        {
            foreach (var r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
