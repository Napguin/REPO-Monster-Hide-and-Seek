using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Third-person view for the monster.
    /// R.E.P.O. renders its cameras itself (CameraUpdate.LateUpdate, at its own update rate),
    /// so the offset is applied around that render call (Harmony) and, as a second path, in the
    /// camera render callbacks. The cameras are always restored right after rendering, so the
    /// game's own camera logic never sees the offset.
    /// </summary>
    internal static class ThirdPersonCamera
    {
        public static bool Active;
        public static float Distance = 3.2f;
        public static float Height = 0.7f;

        // diagnostics
        public static string MainCameraName = "-";
        public static int AppliedFrame = -1000;
        public static int CamerasMoved;
        public static int GameCameraCount;

        /// <summary>True if the offset was really applied to a rendered camera in the last ~second.</summary>
        public static bool Working => Active && Time.frameCount - AppliedFrame < 90;

        private static readonly Dictionary<Camera, Vector3> Saved = new Dictionary<Camera, Vector3>();
        private static readonly HashSet<Camera> GameCams = new HashSet<Camera>();
        private static bool _hooked;
        private static float _smoothedDist = 3.2f;
        private static int _offsetFrame = -1, _camsFrame = -1;
        private static Vector3 _offset;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += PreRender;
            Camera.onPostRender += PostRender;
            RenderPipelineManager.beginCameraRendering += (ctx, cam) => PreRender(cam);
            RenderPipelineManager.endCameraRendering += (ctx, cam) => PostRender(cam);
        }

        // ------------------------------------------------------------------
        // Which cameras belong to the player view
        // ------------------------------------------------------------------
        private static void RefreshGameCams()
        {
            if (_camsFrame == Time.frameCount) return;
            _camsFrame = Time.frameCount;
            GameCams.Clear();

            var main = GameRefs.MainCamera;
            if (main != null) GameCams.Add(main);
            MainCameraName = main != null ? main.name : "NOT FOUND";

            AddCams(Compat.Get<List<Camera>>(Compat.Static("RenderTextureMain", "instance"), "cameras"));
            AddCams(Compat.Get<List<Camera>>(Compat.Static("CameraZoom", "Instance"), "cams"));

            // Cameras sitting on the same spot as the main camera (stacked overlay cameras).
            if (main != null)
                foreach (var c in Camera.allCameras)
                    if (c != null && (c.transform.position - main.transform.position).sqrMagnitude < 0.04f) GameCams.Add(c);

            GameCameraCount = GameCams.Count;
        }

        private static void AddCams(List<Camera> cams)
        {
            if (cams == null) return;
            foreach (var c in cams) if (c != null) GameCams.Add(c);
        }

        private static Transform ReferenceTransform()
        {
            var main = GameRefs.MainCamera;
            if (main != null) return main.transform;
            foreach (var c in GameCams) if (c != null) return c.transform;
            return null;
        }

        // ------------------------------------------------------------------
        // Apply / restore
        // ------------------------------------------------------------------
        private static void Offset(Camera cam)
        {
            if (cam == null || Saved.ContainsKey(cam)) return;
            if (_offsetFrame != Time.frameCount)
            {
                var reference = ReferenceTransform();
                if (reference == null) return;
                _offsetFrame = Time.frameCount;
                _offset = ComputeOffset(reference);
                CamerasMoved = 0;
            }
            Saved[cam] = cam.transform.position;
            cam.transform.position += _offset;
            AppliedFrame = Time.frameCount;
            CamerasMoved++;
        }

        private static void Restore(Camera cam)
        {
            if (cam != null && Saved.TryGetValue(cam, out var pos))
            {
                cam.transform.position = pos;
                Saved.Remove(cam);
            }
        }

        private static void PreRender(Camera cam)
        {
            if (!Active || cam == null) return;
            RefreshGameCams();
            if (GameCams.Contains(cam)) Offset(cam);
        }

        private static void PostRender(Camera cam) => Restore(cam);

        /// <summary>Around R.E.P.O.'s own camera render step.</summary>
        internal static void BeforeGameRender(List<Camera> cams)
        {
            if (!Active || cams == null) return;
            RefreshGameCams();
            foreach (var c in cams) { if (c != null) { GameCams.Add(c); Offset(c); } }
        }

        internal static void AfterGameRender(List<Camera> cams)
        {
            if (cams == null) return;
            foreach (var c in cams) Restore(c);
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

            // If a wall forces the camera very close, lift it less so we don't end up inside the head.
            return Vector3.up * Height * Mathf.Clamp01(_smoothedDist / Mathf.Max(0.01f, want)) + dir * _smoothedDist;
        }

        public static void RestoreAll()
        {
            foreach (var kv in Saved)
                if (kv.Key != null) kv.Key.transform.position = kv.Value;
            Saved.Clear();
        }
    }

    [HarmonyPatch(typeof(CameraUpdate), nameof(CameraUpdate.LateUpdate))]
    internal static class CameraUpdatePatch
    {
        private static void Prefix(CameraUpdate __instance)
        {
            try { ThirdPersonCamera.BeforeGameRender(Compat.Get<List<Camera>>(__instance, "cams")); }
            catch (System.Exception ex) { Plugin.Log.LogDebug("TP prefix: " + ex.Message); }
        }

        private static void Postfix(CameraUpdate __instance)
        {
            try { ThirdPersonCamera.AfterGameRender(Compat.Get<List<Camera>>(__instance, "cams")); }
            catch (System.Exception ex) { Plugin.Log.LogDebug("TP postfix: " + ex.Message); }
        }
    }
}
