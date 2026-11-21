using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Third-person view for hiders (V, during a round). The game draws the own robot as a shadow-only body in first
    /// person – in third person it is switched to a normal, visible body and back afterwards.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private bool _hiderThirdPerson;
        private readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> _ownBodyModes =
            new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();

        /// <summary>Per frame for a local hider. Returns true when the hider camera is in charge.</summary>
        private bool HiderCameraTick()
        {
            if (!RoundActive || !LocalHider(out var me) || !IsAlive(me)) { ShowOwnBody(false); return false; }

            if (!(_menuOpen || _debugOpen || _animTunerOpen || _freePickOpen || GameRefs.IsTyping) && Pressed(_viewAction))
            {
                _hiderThirdPerson = !_hiderThirdPerson;
                Plugin.Log.LogInfo($"[Hider] view -> {(_hiderThirdPerson ? "third" : "first")} person");
            }
            ThirdPersonCamera.Active = _hiderThirdPerson;
            ThirdPersonCamera.Distance = Mathf.Max(2.2f, Plugin.CameraDistance.Value * 0.7f);
            ThirdPersonCamera.Height = 0.6f;
            ShowOwnBody(ThirdPersonCamera.Working);
            return true;
        }

        private void ShowOwnBody(bool show)
        {
            var me = GameRefs.LocalAvatar;
            if (show && me != null)
            {
                var roots = new List<Transform> { me.transform };
                if (me.playerAvatarVisuals != null) roots.Add(me.playerAvatarVisuals.transform);
                foreach (var root in roots)
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                        if (!_ownBodyModes.ContainsKey(r)) _ownBodyModes[r] = r.shadowCastingMode;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                return;
            }
            if (_ownBodyModes.Count == 0) return;
            foreach (var kv in _ownBodyModes) if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
            _ownBodyModes.Clear();
        }
    }
}
