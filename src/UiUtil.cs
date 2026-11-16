using UnityEngine;

namespace HideAndSeekMonster
{
    internal static class UiUtil
    {
        /// <summary>
        /// World position -> GUI position. R.E.P.O. renders into a small texture that is shown with its own aspect
        /// ratio, with black bars when the window has another shape – so the picture doesn't fill the whole screen.
        /// </summary>
        public static bool WorldToGui(Camera cam, Vector3 world, out Vector2 gui)
        {
            gui = Vector2.zero;
            Vector3 vp = cam.WorldToViewportPoint(world);
            if (vp.z <= 0f) return false;
            float aspect = cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : (float)Screen.width / Screen.height;
            float sw = Screen.width, sh = Screen.height;
            float w = sw, h = sh, x0 = 0f, y0 = 0f;
            if (sw / sh > aspect) { w = sh * aspect; x0 = (sw - w) / 2f; } // bars left / right
            else { h = sw / aspect; y0 = (sh - h) / 2f; }               // bars top / bottom
            gui = new Vector2(x0 + vp.x * w, y0 + (1f - vp.y) * h);
            return true;
        }

        /// <summary>Scroll view that only ever scrolls up / down – never shows a left / right scrollbar.</summary>
        public static Vector2 VScroll(Rect view, Vector2 pos, Rect content)
        {
            content.width = Mathf.Min(content.width, view.width - 16f);
            pos.x = 0f;
            return GUI.BeginScrollView(view, pos, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        }
    }
}
