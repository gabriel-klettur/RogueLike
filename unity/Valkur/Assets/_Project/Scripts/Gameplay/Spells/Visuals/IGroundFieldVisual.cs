using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A visual a persistent ground field brings with it, instead of taking
    /// <see cref="AreaFXRig"/>'s concentric discs.
    ///
    /// <para>WHY THE SEAM EXISTS. <c>PuddleController</c> built its rig unconditionally and
    /// from one palette, so every field it drove looked like a lava pool — including the
    /// root field, which then stacked its own green stems on top of four orange sprites and
    /// an orange light. The obvious fix, branching on the spell key inside the controller,
    /// is the same mistake one level up: the executor already had to do that, and a generic
    /// controller that knows the name of one spell is not generic.</para>
    ///
    /// <para>An owner that passes null keeps the historical behaviour exactly — the disc rig
    /// with the palette the controller picks — which is what every other puddle wants.</para>
    /// </summary>
    // Public rather than internal because PuddleController.Initialize is public and takes
    // one: an internal parameter type on a public method is CS0051, and narrowing the
    // method instead would hide it from anything outside this assembly that already calls
    // it. The IMPLEMENTATIONS stay internal.
    public interface IGroundFieldVisual
    {
        /// <summary>
        /// One frame. <paramref name="deltaTime"/> is passed in rather than read off
        /// <c>Time</c> so the rig can be driven from a test or a probe at a chosen step —
        /// a rig that reads the clock itself measures the harness, not itself.
        /// </summary>
        /// <param name="fade">0..1 master alpha, ramped down as the field expires.</param>
        void Tick(float deltaTime, float fade);

        /// <summary>
        /// The field just hurt something at <paramref name="worldTarget"/>. Implementations
        /// that have nothing to say about a single victim may ignore it; the point of the
        /// call is that a damage tick is the only EVENT a persistent field has, and an
        /// effect made entirely of continuous motion stops being read after about a second.
        /// </summary>
        void Lash(Vector3 worldTarget);

        /// <summary>Release anything the rig owns. Safe to call twice.</summary>
        void Destroy();
    }
}
