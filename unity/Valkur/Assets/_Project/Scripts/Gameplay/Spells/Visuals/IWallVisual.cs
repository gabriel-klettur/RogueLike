using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Everything <see cref="WallController"/> needs from whatever draws its barrier.
    ///
    /// <para>WHY A SECOND IMPLEMENTATION RATHER THAN A RECOLOUR. <see cref="IceWallVisual"/>
    /// draws a row of crystal spikes, and that is the vocabulary of ICE: opaque, faceted,
    /// irregular, piled on the ground. Tinting it violet gives a purple ice wall, not a wall
    /// of magic. This project has recorded the same lesson three times — a disc cannot draw a
    /// line (<see cref="IceWallVisual"/> replacing <c>AreaFXRig</c>), a strip cannot fill a
    /// cone (<c>FlameConeFX</c> replacing a <c>LineRenderer</c>), a disc cannot draw a funnel
    /// (<c>VortexFunnelFX</c>) — and the rule behind all three is that the rig has to be
    /// SHAPED like the thing it draws.</para>
    ///
    /// <para>So <see cref="ArcaneBarrierVisual"/> is a different shape: a translucent woven
    /// PLANE pinned between anchor posts, not a pile of spikes. The controller's clock,
    /// health, collider and three exits are identical for both, which is exactly the part
    /// that belongs behind an interface.</para>
    /// </summary>
    internal interface IWallVisual
    {
        /// <summary>Advance the effect. Called every frame by the controller, melt included.</summary>
        void Tick(float deltaTime);

        /// <summary>True once a melt has run its course and the GameObject can be destroyed.</summary>
        bool MeltComplete { get; }

        /// <summary>Accumulated damage, 0 = untouched, 1 = about to die. Idempotent.</summary>
        void SetDamage01(float damage01);

        /// <summary>A blow landed at this world point.</summary>
        void Hit(Vector3 worldPoint);

        /// <summary>
        /// Half the span still standing, world units. The controller shrinks the collider to
        /// it, so what blocks is exactly what is drawn.
        /// </summary>
        float SurvivingHalfSpan();

        /// <summary>The killing blow: fail everything still standing, at once.</summary>
        void Shatter();

        /// <summary>Fade out over <paramref name="seconds"/> and stop emitting.</summary>
        void BeginMelt(float seconds);
    }
}
