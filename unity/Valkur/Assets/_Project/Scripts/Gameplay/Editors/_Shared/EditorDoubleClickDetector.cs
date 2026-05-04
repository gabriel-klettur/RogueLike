using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared double-click detector for runtime in-game editors. Returns
    /// <c>true</c> from <see cref="PollLeftDouble"/> on the second left-button
    /// press within <see cref="_intervalSec"/> seconds and within
    /// <see cref="_tolerancePx"/> screen pixels of the previous press.
    ///
    /// Pointer-over-UI presses are ignored (we only want world double-clicks).
    /// Designed to coexist with single-click handlers: callers that already
    /// react to a single click should still call <see cref="PollLeftDouble"/>
    /// and treat <c>true</c> as a separate event (not a replacement of the
    /// single-click). The single-click handler keeps behaving as before; the
    /// double-click adds the "centre on zone" extra.
    /// </summary>
    public sealed class EditorDoubleClickDetector
    {
        private readonly float _intervalSec;
        private readonly float _tolerancePx;

        private float   _lastClickTime = -10f;
        private Vector2 _lastClickPos;

        public EditorDoubleClickDetector(float intervalSec = 0.4f, float tolerancePx = 25f)
        {
            _intervalSec = Mathf.Max(0.05f, intervalSec);
            _tolerancePx = Mathf.Max(1f,    tolerancePx);
        }

        /// <summary>
        /// Returns true on the FRAME the user double-clicks (first click is
        /// silently swallowed and stored as the pending baseline).
        /// </summary>
        public bool PollLeftDouble()
        {
            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                return false;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            Vector2 pos = MouseInputManager.GetScreenMousePosition();
            float now   = Time.unscaledTime;

            bool isDouble = (now - _lastClickTime) <= _intervalSec
                            && Vector2.Distance(pos, _lastClickPos) <= _tolerancePx;

            // Reset baseline so a third quick click doesn't keep firing.
            _lastClickTime = isDouble ? -10f : now;
            _lastClickPos  = pos;
            return isDouble;
        }

        /// <summary>Forget the last click. Call this on editor activate / deactivate.</summary>
        public void Reset()
        {
            _lastClickTime = -10f;
            _lastClickPos  = Vector2.zero;
        }
    }
}
