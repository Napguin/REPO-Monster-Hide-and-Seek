using UnityEngine;

namespace HideAndSeekMonster
{
    internal static class UiUtil
    {
        /// <summary>Scroll view that only ever scrolls up / down – never shows a left / right scrollbar.</summary>
        public static Vector2 VScroll(Rect view, Vector2 pos, Rect content)
        {
            content.width = Mathf.Min(content.width, view.width - 16f);
            pos.x = 0f;
            return GUI.BeginScrollView(view, pos, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        }
    }
}
