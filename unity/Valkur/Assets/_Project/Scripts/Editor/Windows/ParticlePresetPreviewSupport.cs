using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Decides whether a particle preset can be previewed outside Play Mode, and says
    /// why when it cannot.
    ///
    /// Split out of <c>ParticlePresetDefinitionEditor</c> on purpose: the rendering is
    /// untestable in EditMode (it needs a live PreviewRenderUtility), but the decision
    /// is pure and is exactly the part that will rot. When a kind gains or loses
    /// edit-mode support, this is the one place to change and the one place to test.
    /// </summary>
    public static class ParticlePresetPreviewSupport
    {
        /// <summary>The one kind that draws with a LineRenderer driven by a coroutine.</summary>
        public const string LIGHTNING_KIND = "lightning";

        /// <summary>
        /// True when the preset can be simulated and drawn by the inspector preview.
        /// </summary>
        public static bool IsPreviewable(ParticlePresetDefinition preset)
            => UnsupportedReason(preset) == null;

        /// <summary>
        /// Why <paramref name="preset"/> cannot be previewed, or <c>null</c> when it can.
        /// The string is shown to the user in the preview area, so it says what to do
        /// rather than merely what failed.
        /// </summary>
        public static string UnsupportedReason(ParticlePresetDefinition preset)
        {
            if (preset == null)
                return "No preset selected.";

            if (preset.vfx == null)
                return "This preset has no VFX parameters.";

            // ParticleEmitter drives lightning from a while(true) coroutine, and
            // coroutines do not advance outside Play Mode — the bolt would be applied
            // and then sit frozen, which reads as a broken preview rather than an
            // unsupported one. Converting it to a tick method would lift this.
            if (preset.vfx.kind == LIGHTNING_KIND)
                return "The 'lightning' kind is coroutine-driven and only animates in Play Mode.\n" +
                       "Open the Particles Editor (F1) to see it.";

            return null;
        }

        /// <summary>
        /// Orthographic half-height that frames a preset without having simulated it yet.
        /// Derived from the preset's own reach rather than a fixed number, so a wide
        /// water_flow and a tight spark both arrive framed on the first repaint instead
        /// of popping once bounds exist.
        /// </summary>
        public static float InitialOrthoSize(ParticlePresetDefinition preset)
        {
            const float MIN = 1.5f;
            const float MAX = 8f;
            if (preset?.vfx == null) return MIN;

            var v = preset.vfx;
            // Whichever dominates: the emission shape, or how far a particle travels.
            float reach = UnityEngine.Mathf.Max(v.radius, v.outerRadius);
            reach = UnityEngine.Mathf.Max(reach, v.speed * v.lifespan);
            reach += v.sizeMax;

            return UnityEngine.Mathf.Clamp(reach * 1.25f, MIN, MAX);
        }
    }
}
