using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>Robust access to things R.E.P.O. doesn't expose the "standard Unity way".</summary>
    internal static class GameRefs
    {
        /// <summary>R.E.P.O.'s camera is not tagged MainCamera, so Camera.main is usually null.</summary>
        public static Camera MainCamera
        {
            get
            {
                var c = Compat.Get<Camera>(Compat.Static("CameraUtils", "Instance"), "MainCamera");
                if (c != null) return c;
                c = Compat.Get<Camera>(Compat.Static("GameDirector", "instance"), "MainCamera");
                if (c != null) return c;
                return Camera.main;
            }
        }

        public static Transform ViewTransform(PlayerAvatar fallback)
        {
            var c = MainCamera;
            if (c != null) return c.transform;
            var go = Compat.Get<GameObject>(PlayerController.instance, "cameraGameObject");
            if (go != null) return go.transform;
            return fallback != null ? fallback.transform : null;
        }

        public static PlayerAvatar LocalAvatar
        {
            get
            {
                if (PlayerAvatar.instance != null) return PlayerAvatar.instance;
                return Compat.Call(typeof(SemiFunc), "PlayerAvatarLocal", out var r) ? r as PlayerAvatar : null;
            }
        }

        public static bool IsLocal(PlayerAvatar p)
        {
            if (p == null) return false;
            if (p.isLocal) return true;
            var local = LocalAvatar;
            return local != null && local == p;
        }

        /// <summary>Only blocks monster controls while the player is actually typing.</summary>
        public static bool IsTyping
        {
            get
            {
                var chat = Compat.Static("ChatManager", "instance");
                if (chat == null) return false;
                var state = Compat.Get<object>(chat, "chatState");
                return state != null && state.ToString() == "Active";
            }
        }
    }
}
