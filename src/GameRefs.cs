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
                try { if (CameraUtils.Instance != null && CameraUtils.Instance.MainCamera != null) return CameraUtils.Instance.MainCamera; } catch { }
                try { if (GameDirector.instance != null && GameDirector.instance.MainCamera != null) return GameDirector.instance.MainCamera; } catch { }
                return Camera.main;
            }
        }

        public static Transform ViewTransform(PlayerAvatar fallback)
        {
            var c = MainCamera;
            if (c != null) return c.transform;
            if (PlayerController.instance != null && PlayerController.instance.cameraGameObject != null)
                return PlayerController.instance.cameraGameObject.transform;
            return fallback != null ? fallback.transform : null;
        }

        public static PlayerAvatar LocalAvatar
        {
            get
            {
                if (PlayerAvatar.instance != null) return PlayerAvatar.instance;
                try { return SemiFunc.PlayerAvatarLocal(); } catch { return null; }
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
                try { return ChatManager.instance != null && ChatManager.instance.chatState == ChatManager.ChatState.Active; }
                catch { return false; }
            }
        }
    }
}
