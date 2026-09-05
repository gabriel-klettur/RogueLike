using UnityEngine;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Decides whether the InputSystem mouse has stopped receiving events, by watching it
    /// against the legacy backend over time.
    ///
    /// <para>The Unity 2022.3 Editor intermittently stops delivering OS events to the
    /// InputSystem while <c>UnityEngine.Input</c> keeps working. <c>MouseInputManager</c>
    /// already ORs the two backends for buttons, and for POSITION it guarded exactly one
    /// shape of the failure: an InputSystem that reads <c>(0,0)</c> while legacy reads
    /// something else. Measured live, the device does not always freeze at zero — it froze at
    /// the screen CENTRE (<c>(800,400)</c> on a 1600x800 view) and at whatever the last
    /// delivered position was. A frozen non-zero position is finite, in view and plausible,
    /// so the selector trusted it over the live legacy reading, the cursor resolved to the
    /// player's own feet, and every aimed spell flew straight down while the pointer sat
    /// elsewhere. Intermittent by nature: it depends on which value the device happened to
    /// stop on, which is why "it works again" proves nothing.</para>
    ///
    /// <para>A single frame cannot tell a frozen position from a still hand, so this is a
    /// tracker rather than a predicate: the InputSystem is declared frozen once the legacy
    /// pointer has MOVED across <see cref="FramesToDeclareFrozen"/> distinct frames while the
    /// InputSystem position has not changed at all. The moment the InputSystem moves again the
    /// verdict is cleared — the device is alive and is the preferred backend once more.</para>
    ///
    /// <para>Pure and frame-keyed so EditMode tests drive it with explicit frame numbers.
    /// Repeated observations inside one frame are ignored: every reader in the project calls
    /// <c>TryGetScreenMousePosition</c>, often several times a frame, and counting those as
    /// evidence would declare a freeze from a single frame of motion.</para>
    /// </summary>
    public sealed class MouseFreezeTracker
    {
        /// <summary>Distinct frames of legacy motion with a static InputSystem before it is frozen.</summary>
        public const int FramesToDeclareFrozen = 3;

        /// <summary>Legacy pointer motion below this many pixels is a still hand, not evidence.</summary>
        public const float LegacyMotionThreshold = 1f;

        private const float InputSystemMotionEpsilon = 0.01f;

        private bool _hasSample;
        private int _lastFrame = int.MinValue;
        private Vector2 _lastInputSystem;
        private Vector2 _lastLegacy;
        private int _evidence;

        /// <summary>True while the InputSystem position is considered frozen.</summary>
        public bool InputSystemFrozen { get; private set; }

        /// <summary>
        /// Feed this frame's readings and answer whether the InputSystem is frozen.
        ///
        /// <para>Without both backends there is no second opinion, so the answer is false —
        /// a missing device is handled by the selector's own finiteness checks.</para>
        /// </summary>
        public bool Observe(int frame, Vector2 inputSystem, bool hasInputSystem,
                            Vector2 legacy, bool hasLegacy)
        {
            if (!hasInputSystem || !hasLegacy)
            {
                Reset();
                return false;
            }

            if (frame == _lastFrame) return InputSystemFrozen;
            _lastFrame = frame;

            if (!_hasSample)
            {
                _hasSample = true;
                _lastInputSystem = inputSystem;
                _lastLegacy = legacy;
                return InputSystemFrozen;
            }

            bool inputSystemMoved = (inputSystem - _lastInputSystem).sqrMagnitude
                                    > InputSystemMotionEpsilon * InputSystemMotionEpsilon;
            bool legacyMoved = (legacy - _lastLegacy).sqrMagnitude
                               > LegacyMotionThreshold * LegacyMotionThreshold;

            _lastInputSystem = inputSystem;
            _lastLegacy = legacy;

            if (inputSystemMoved)
            {
                // Alive. Whatever the verdict was, the device is delivering again.
                _evidence = 0;
                InputSystemFrozen = false;
                return false;
            }

            if (legacyMoved)
            {
                _evidence++;
                if (_evidence >= FramesToDeclareFrozen) InputSystemFrozen = true;
            }

            // Neither moved: a still hand says nothing either way. The verdict stands.
            return InputSystemFrozen;
        }

        /// <summary>Forget everything. Used on domain reload and by tests.</summary>
        public void Reset()
        {
            _hasSample = false;
            _lastFrame = int.MinValue;
            _lastInputSystem = Vector2.zero;
            _lastLegacy = Vector2.zero;
            _evidence = 0;
            InputSystemFrozen = false;
        }
    }
}
