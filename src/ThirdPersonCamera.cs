using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Third-person view for the monster: just before each of the player's game cameras renders,
    /// it is pulled back behind the player (and restored right after), so the game's own camera
    /// logic, post-processing and render textures keep working untouched.
    /// Hooks both the built-in pipeline (Camera.onPreCull) and SRP (RenderPipelineManager).
    /// </summary>
    internal static class ThirdPersonCamera
    {
        public static bool Active;
        public static float Distance = 3.2f;
        public static float Height = 0.7f;

        // diagnostics
        public static string MainCameraName = "-";
        public static int AppliedFrame = -1;
        public static int CamerasMoved;

        private static readonly Dictionary<Camera, Vector3> Saved = new Dictionary<Camera, Vector3>();
        private static bool _hooked;
        private static float _smoothedDist = 3.2f;
        private static int _offsetFrame = -1;
        private static Vector3 _offset;
        private static Vector3 _mainPosThisFrame;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += PreRender;
            Camera.onPostRender += PostRender;
            RenderPipelineManager.beginCameraRendering += (ctx, cam) => PreRender(cam);
            RenderPipelineManager.endCameraRendering += (ctx, cam) => PostRender(cam);
        }

        private static void PreRender(Camera cam)
        {
            if (!Active || cam == null || Saved.ContainsKey(cam)) return;
            var main = GameRefs.MainCamera;
            if (main == null) { MainCameraName = "NOT FOUND"; return; }
            MainCameraName = main.name;

            if (_offsetFrame != Time.frameCount)
            {
                _offsetFrame = Time.frameCount;
                _mainPosThisFrame = main.transform.position;
                _offset = ComputeOffset(main.transform);
                CamerasMoved = 0;
            }
            // Only the player's own game cameras: main + any camera stacked on it at the same spot.
            if (cam != main && (cam.transform.position - _mainPosThisFrame).sqrMagnitude > 0.04f) return;

            Saved[cam] = cam.transform.position;
            cam.transform.position += _offset;
            AppliedFrame = Time.frameCount;
            CamerasMoved++;
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
            Vector3 pivot = camT.position + Vector3.up * Height;
            Vector3 dir = -camT.forward;
            float want = Distance;

            float hitDist = want;
            var hits = Physics.SphereCastAll(pivot, 0.25f, dir, want, ~0, QueryTriggerInteraction.Ignore);
            var local = GameRefs.LocalAvatar;
            foreach (var h in hits)
            {
                if (h.collider == null || h.distance <= 0f) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // props
                if (local != null && h.collider.transform.root == local.transform.root) continue;
                if (PlayerController.instance != null && h.collider.transform.root == PlayerController.instance.transform.root) continue;
                if (h.collider.GetComponentInParent<PlayerAvatar>() != null) continue;
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                hitDist = Mathf.Min(hitDist, h.distance);
            }
            float target = Mathf.Max(0.3f, hitDist - 0.1f);
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
