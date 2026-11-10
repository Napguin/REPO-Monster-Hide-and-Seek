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
        public static Vector3 LastOffset => _offset;

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
            // Never lift the camera through the ceiling (tall monsters like Elsa).
            float height = Height;
            float up = NearestHit(camT.position, Vector3.up, Height + 0.4f);
            if (up < Height + 0.4f) height = Mathf.Max(0f, up - 0.4f);
            Vector3 pivot = camT.position + Vector3.up * height;
            Vector3 dir = -camT.forward;
            float want = Distance;

            float hitDist = NearestHit(pivot, dir, want);
            float target = hitDist <= 0.01f ? 0f : Mathf.Max(0.3f, hitDist - 0.1f); // pressed against geometry: stay at the head
            _smoothedDist = target < _smoothedDist ? target : Mathf.Lerp(_smoothedDist, target, 1f - Mathf.Exp(-6f * Time.deltaTime));

            // If a wall forces the camera very close, lift it less so we don't end up inside the head.
            return Vector3.up * height * Mathf.Clamp01(_smoothedDist / Mathf.Max(0.01f, want)) + dir * _smoothedDist;
        }

        private static bool IsLevel(Collider c)
        {
            if (c == null) return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false; // props
            var local = GameRefs.LocalAvatar;
            if (local != null && c.transform.root == local.transform.root) return false;
            if (PlayerController.instance != null && c.transform.root == PlayerController.instance.transform.root) return false;
            if (c.GetComponentInParent<PlayerAvatar>() != null) return false;
            if (c.GetComponentInParent<PlayerController>() != null) return false;
            if (c.GetComponentInParent<MonsterVisual>() != null) return false;
            return true;
        }

        /// <summary>Distance to the nearest level geometry (players, props and the monster ignored).</summary>
        private static float NearestHit(Vector3 from, Vector3 dir, float max)
        {
            // Starting inside / touching a wall or ceiling: casts don't see that collider at all → treat as blocked.
            foreach (var c in Physics.OverlapSphere(from, 0.25f, ~0, QueryTriggerInteraction.Ignore))
                if (IsLevel(c)) return 0f;
            float best = max;
            // A thin ray as well: sphere casts can slip past thin ceilings at grazing angles.
            foreach (var h in Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance > 0f && IsLevel(h.collider)) best = Mathf.Min(best, h.distance);
            var local = GameRefs.LocalAvatar;
            foreach (var h in Physics.SphereCastAll(from, 0.25f, dir, max, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.distance <= 0f) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // props
                if (local != null && h.collider.transform.root == local.transform.root) continue;
                if (PlayerController.instance != null && h.collider.transform.root == PlayerController.instance.transform.root) continue;
                if (h.collider.GetComponentInParent<PlayerAvatar>() != null) continue;
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<MonsterVisual>() != null) continue;
                best = Mathf.Min(best, h.distance);
            }
            return best;
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
