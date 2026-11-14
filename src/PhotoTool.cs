using System.Collections;
using System.IO;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Photo tool (debug menu, F7): saves pictures for the mod's pages.
    ///  - Monster portrait: the chosen monster alone in front of a camera, on a transparent background (PNG).
    ///  - Menu screenshots: F8 menu, Monster Showcase, How To Play and the debug menu, one after another.
    ///  - Screenshot in 5 s: anything else (item picker, a scene in the game) – the debug menu closes first.
    /// Files go to BepInEx/HideAndSeekPhotos.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private bool _photoBusy;

        internal static string PhotoFolder
        {
            get
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "HideAndSeekPhotos");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Stamp() => System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ---------------- monster portrait ----------------
        private IEnumerator PhotoMonster(string monster)
        {
            if (_photoBusy) yield break;
            _photoBusy = true;
            GameObject camGo = null, keyGo = null, fillGo = null;
            MonsterVisual mv = null;
            RenderTexture rt = null;
            try
            {
                var me = GameRefs.LocalAvatar;
                Vector3 basePos = (me != null ? me.transform.position : Vector3.zero) + Vector3.up * 400f; // far above, nothing else in view
                mv = MonsterVisual.CreatePhoto(monster, basePos, 180f);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[Photo] model failed: " + ex.Message); }
            if (mv == null) { _photoBusy = false; Banner(L.PhotoFailed, Color.white, 2f); yield break; }

            // let the animator pose it
            yield return null; yield return null; yield return null;

            string file = null;
            try
            {
                var rends = mv.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(mv.transform.position + Vector3.up, Vector3.one);
                bool any = false;
                foreach (var r in rends)
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.3f);

                camGo = new GameObject("HNS_PhotoCam");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.fieldOfView = 28f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = size * 20f + 20f;
                // a bit from the front-left and slightly above, like a character portrait
                Vector3 dir = Quaternion.Euler(-12f, -25f, 0f) * (mv.transform.forward);
                float dist = size * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
                camGo.transform.position = b.center + dir * dist;
                camGo.transform.LookAt(b.center);

                keyGo = new GameObject("HNS_PhotoKey");
                var key = keyGo.AddComponent<Light>();
                key.type = LightType.Directional; key.intensity = 1.3f; key.color = new Color(1f, 0.96f, 0.9f);
                keyGo.transform.rotation = Quaternion.LookRotation((b.center - camGo.transform.position).normalized + Vector3.down * 0.6f);
                fillGo = new GameObject("HNS_PhotoFill");
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Point; fill.range = size * 6f + 4f; fill.intensity = 2f; fill.color = new Color(0.8f, 0.85f, 1f);
                fillGo.transform.position = b.center + Quaternion.Euler(0, 120f, 0) * mv.transform.forward * size * 1.5f + Vector3.up * size;

                const int px = 1024;
                rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                file = Path.Combine(PhotoFolder, $"monster_{SafeName(monster)}_{Stamp()}.png");
                File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
                Destroy(tex);
                Plugin.Log.LogInfo($"[Photo] saved {file}");
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[Photo] render failed: " + ex); }
            finally
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (camGo != null) Destroy(camGo);
                if (keyGo != null) Destroy(keyGo);
                if (fillGo != null) Destroy(fillGo);
                if (mv != null) mv.Remove();
                _photoBusy = false;
            }
            Banner(file != null ? L.PhotoSaved(Path.GetFileName(file)) : L.PhotoFailed, Color.white, 3f);
        }

        // ---------------- screenshots ----------------
        private IEnumerator Shot(string name)
        {
            yield return null; yield return new WaitForEndOfFrame(); yield return null; // let the GUI draw it
            string file = Path.Combine(PhotoFolder, $"{SafeName(name)}_{Stamp()}.png");
            ScreenCapture.CaptureScreenshot(file);
            yield return null; yield return null;
            Plugin.Log.LogInfo($"[Photo] screenshot {file}");
        }

        /// <summary>F8 menu, Monster Showcase (a few entries), How To Play and the debug menu, one after another.</summary>
        private IEnumerator PhotoMenus()
        {
            if (_photoBusy) yield break;
            _photoBusy = true;
            _bannerUntil = 0f;
            bool dbg = _debugOpen;
            _debugOpen = false;

            _menuOpen = true; SetMenuPage(MenuPage.Main);
            yield return Shot("menu_f8");

            SetMenuPage(MenuPage.Showcase);
            foreach (var key in new[] { "attack", "bowtie", "pony", "tick", "balloon" })
            {
                for (int i = 0; i < ShowcaseEntries.Length; i++) if (ShowcaseEntries[i].Key == key) _showcaseSel = i;
                _showcaseStart = Time.realtimeSinceStartup - 1.2f; // a frame in the middle of the animation
                yield return new WaitForSeconds(0.3f);
                yield return Shot("menu_showcase_" + key);
            }

            SetMenuPage(MenuPage.HowToPlay);
            _htpPage = 1;
            yield return new WaitForSeconds(0.3f);
            yield return Shot("menu_howtoplay");

            SetMenuPage(MenuPage.Main);
            _menuOpen = false;
            _debugOpen = true;
            yield return Shot("menu_debug");
            _debugOpen = dbg;
            _photoBusy = false;
            Banner(L.PhotoMenusDone, Color.white, 3f);
        }

        /// <summary>The debug menu closes, 5 s later the screen is saved (open the item picker, stand somewhere ...).</summary>
        private IEnumerator PhotoDelayed()
        {
            if (_photoBusy) yield break;
            _photoBusy = true;
            _debugOpen = false;
            for (int s = 5; s > 0; s--)
            {
                Banner(L.PhotoIn(s), Color.white, 0.9f);
                yield return new WaitForSeconds(1f);
            }
            _bannerUntil = 0f;
            yield return Shot("screenshot");
            _photoBusy = false;
            Banner(L.PhotoSaved("screenshot"), Color.white, 2f);
        }
    }
}
