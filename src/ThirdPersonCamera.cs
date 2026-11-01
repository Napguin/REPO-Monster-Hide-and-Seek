using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Third-person view for the monster: just before each game camera renders, it is pulled
    /// back behind the player (and restored right after), so the game's own camera logic,
    /// post-processing and render textures keep working untouched.
    /// </summary>
    internal static class ThirdPersonCamera
    {
        public static bool Active;
        public static float Distance = 3.2f;
        public static float Height = 0.7f;

        private static readonly Dictionary<Camera, Vector3> Saved = new Dictionary<Camera, Vector3>();
        private static bool _hooked;
        private static float _smoothedDist = 3.2f;
        private static int _lastFrame = -1;
        private static Vector3 _offset;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += PreCull;
            Camera.onPostRender += PostRender;
        }

        private static void PreCull(Camera cam)
        {
            if (!Active || cam == null) return;
            var main = Camera.main;
            if (main == null) return;
            // Only the player's own game cameras (main + any camera stacked on it at the same spot).
            if (cam != main && (cam.transform.position - main.transform.position).sqrMagnitude > 0.01f) return;

            if (_lastFrame != Time.frameCount)
            {
                _lastFrame = Time.frameCount;
                _offset = ComputeOffset(main.transform);
            }
            Saved[cam] = cam.transform.position;
            cam.transform.position += _offset;
        }

        private static void PostRender(Camera cam)
        {
            if (cam != null && Saved.TryGetValue(cam, out var pos))
            {
                cam.transform.position = pos;
                Saved.Remove(cam);
            }
        }

        private static Vector3 ComputeOffset(Transform camT)
        {
            Vector3 origin = camT.position;
            Vector3 pivot = origin + Vector3.up * Height;
            Vector3 dir = -camT.forward;
            float want = Distance;

            // Don't clip through walls: find the closest level geometry behind us.
            float hitDist = want;
            var hits = Physics.SphereCastAll(pivot, 0.25f, dir, want, ~0, QueryTriggerInteraction.Ignore);
            var local = PlayerAvatar.instance;
            foreach (var h in hits)
            {
                if (h.collider == null || h.distance <= 0f) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // props
                if (local != null && h.collider.transform.root == local.transform.root) continue;
                if (PlayerController.instance != null && h.collider.transform.root == PlayerController.instance.transform.root) continue;
                if (h.collider.GetComponentInParent<PlayerAvatar>() != null) continue;
                hitDist = Mathf.Min(hitDist, h.distance);
            }
            float target = Mathf.Max(0.3f, hitDist - 0.1f);
            // pull in instantly, ease back out
            _smoothedDist = target < _smoothedDist ? target : Mathf.Lerp(_smoothedDist, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            return Vector3.up * Height + dir * _smoothedDist;
        }

        public static void RestoreAll()
        {
            foreach (var kv in Saved)
                if (kv.Key != null) kv.Key.transform.position = kv.Value;
            Saved.Clear();
        }
    }
}
