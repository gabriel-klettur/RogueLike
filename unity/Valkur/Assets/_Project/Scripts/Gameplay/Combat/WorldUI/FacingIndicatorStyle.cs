using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Every number the ground aim indicator is tuned by, in one place.
    ///
    /// <para>Deliberately constants in code rather than a <c>ScriptableObject</c>: the
    /// indicator is not designer-authored CONTENT the way a spell or a particle preset is —
    /// there is exactly one of it, it ships with the game, and an asset for it would be a
    /// file nobody ever opens plus an inspector slot on a component that is
    /// <c>AddComponent</c>-ed and therefore has no way to be wired from a scene. That is the
    /// same trap <c>ChatSystem._catalog</c> fell into.</para>
    ///
    /// <para><b>There is no constant here for readiness, cast phase or posture, and that is
    /// the design rather than an omission.</b> The rig briefly carried all three — a sweep that
    /// dimmed while the primary recovered, a tip that contracted during a cast wind-up, a ring
    /// instead of a point in Peace — and each was individually defensible and collectively made
    /// the one thing under the character mean four things at once. It answers WHERE YOU POINT
    /// and nothing else. The source guard in <c>FacingIndicatorRigTests</c> fails on a
    /// reference to the stance, the caster or a cooldown, because every one of those arrives
    /// as a small, reasonable-looking addition.</para>
    ///
    /// <para><b>The PULSE is the one exception, and the line it sits on is worth stating.</b>
    /// A STATE has to be read — a dimmed sweep meaning "your spell is recovering" is a second
    /// readout competing with the first. An EVENT tells the player nothing they did not just
    /// do: a flash on the frame they cast, or on each blow of a pick, is confirmation at the
    /// place they are already looking. The test is whether anyone would ever have to LOOK at
    /// the rig to learn something. And the pulse is PUSHED IN — <c>FacingIndicator.Pulse()</c>
    /// is called by whoever acted — so the indicator still does not know what a spell or a
    /// tree is, which is what keeps the source guard green.</para>
    /// </summary>
    internal static class FacingIndicatorStyle
    {
        // ── Geometry ──────────────────────────────────────────────────────────────────
        //
        // The whole rig lies on the FLOOR, so one parent carries the vertical squash and the
        // aim rotation is a CHILD of it. Squashing each piece separately foreshortens its
        // LENGTH without turning its direction, and the piece then slides across the floor
        // instead of lying on it — the same split VortexFunnelFX's ground layers use.

        /// <summary>Vertical squash of the ground plane. A circle drawn through it reads as
        /// an ellipse lying on the floor rather than a disc standing up facing the camera.</summary>
        public const float GroundSquash = 0.42f;

        /// <summary>
        /// Where the tip sits, measured from the feet before the ground squash. FIXED ON
        /// PURPOSE — it does NOT encode the reach of the spell being aimed. A marker whose
        /// distance tracked the primary's <c>range</c> would move every time the player swapped
        /// spells, and a change with no cause the player has connected reads as a glitch rather
        /// than as information.
        /// </summary>
        public const float TipRadius = 1.05f;

        /// <summary>World size of the tip chevron.</summary>
        public const float TipSize = 0.52f;

        // ── Sprite generation (baked into the shared textures, so they are constants) ──

        /// <summary>How far the aura's glow reaches past the chevron's edge, in the sprite's
        /// own normalized space. It is generated from the SAME two segments as the tip, so the
        /// glow is chevron-shaped rather than a round halo sitting behind an arrow.</summary>
        public const float AuraSpread = 0.42f;

        // ── Colour ────────────────────────────────────────────────────────────────────
        //
        // THE RIG IS WHITE. It used to take the primary spell's palette — so a fireball drew a
        // red indicator — and that made the aim marker change colour as a side effect of a
        // loadout choice, competing with every coloured thing the game throws.
        //
        // It is also just TWO layers now: a hard chevron and the glow around it. It used to
        // carry a light pool at the feet and a 60-degree wedge sweeping the ground between the
        // two, which read as a cone of particles growing out of the character's boots and was
        // cut for exactly that. What the pool was FOR still matters and is worth knowing before
        // anyone reaches for it again: it anchored the marker to the body, and without it the
        // first live capture read as an arrowhead floating a unit away with nothing connecting
        // the two. The aura is what carries that job now — a glow big enough to belong to the
        // chevron instead of a second object beside it.
        //
        // Both layers are ADDITIVE, so alpha is COVERAGE and colour is brightness. THERE IS NO
        // DARK OUTLINE ANY MORE and that is a real trade, made deliberately: the rig had one,
        // it was the only reason a white chevron kept a silhouette over pale stone, and a white
        // aura cannot do that job — white saturates all three channels at once and erases
        // whatever it lies on. On dark ground the glow reads better than the outline ever did;
        // on a pale cobbled street the chevron is softer than it was. Bringing the rim back is
        // one layer, not a redesign.

        /// <summary>Faintly cool white — the point.</summary>
        public static readonly Color TipTone = new Color(0.97f, 0.99f, 1.00f, 1f);

        /// <summary>Neutral white — the glow around it.</summary>
        public static readonly Color AuraTone = new Color(1.00f, 1.00f, 1.00f, 1f);

        public const float TipAlpha = 0.92f;
        public const float AuraAlpha = 0.62f;

        /// <summary>Brightness multiplier on the tip. HDR is on and the material is additive,
        /// so this is the intensity dial; alpha would shrink the soft edge instead of dimming
        /// it, which is why it is not the dial.</summary>
        public const float TipGain = 1.15f;

        /// <summary>Brightness multiplier on the aura. Below the tip on purpose — the chevron
        /// has to stay the brightest thing or the glow swallows the shape it exists to frame.
        /// `Aura_IsDimmerThanTheTip` pins that ordering rather than the values.</summary>
        public const float AuraGain = 0.60f;

        /// <summary>How much larger the aura is drawn than the tip. Its sprite already spreads
        /// past the chevron, so this is a second, coarser reach on top of that.</summary>
        public const float AuraGrow = 1.5f;

        // ── Pulse ─────────────────────────────────────────────────────────────────────
        //
        // Hard onset, quadratic decay, and deliberately NOT eased in: an effect made only of
        // continuous motion stops being read after about a second, and what resets the eye is a
        // discontinuity. The same shape the vortex's discharges use, and the opposite of the
        // old chevron's endless sin(t*3) breathing.
        //
        // ONE pulse for every action, on purpose. A cast and a pick strike look identical here
        // because the player already knows which button they pressed; giving them different
        // looks would turn the pulse into a readout of WHICH action, which is the second job
        // this rig exists without.

        /// <summary>How long one pulse lasts. Short enough that harvest blows at the shipped
        /// intervals read as separate beats rather than as a rig that is permanently lit.</summary>
        public const float PulseSeconds = 0.16f;

        /// <summary>
        /// Peak brightness multiplier. Colour, not alpha — on an additive material alpha is
        /// coverage, so pulsing it would make the rig briefly WIDER, not brighter.
        ///
        /// <para>ON ITS OWN THIS IS INVISIBLE, and that is measured rather than feared. The tip
        /// already CLIPS at rest: colour 1.15 times alpha 0.92 is a contribution of 1.06, added
        /// to mid-grey stone at ~0.35, so it is pure white on screen before any pulse. Driving
        /// it to 2.13 clips to exactly the same white — two captures a frame apart were
        /// indistinguishable. It is kept because it is free and it DOES read on dark ground at
        /// night, but the channel that carries the pulse in daylight is the scale below.</para>
        /// </summary>
        public const float PulseGain = 1.9f;

        /// <summary>
        /// How much larger the tip glyph gets at the peak, as a fraction of its size. THE
        /// CHANNEL THAT ACTUALLY READS: a size cannot clip the way an additive colour can, so
        /// this works on pale stone and in a dark cave alike.
        ///
        /// <para>It is the tip's own GLYPH, never the sweep's diameter. The sweep's size is the
        /// one number this rig promises does not move — growing it, even for a sixth of a
        /// second, would put reach back into play.</para>
        /// </summary>
        public const float PulseTipScale = 0.45f;

        /// <summary>How far the tip kicks outward at the peak, in world units. Small: the
        /// radius is fixed and a kick large enough to read as a size change would put the one
        /// number this rig promises not to move back into play.</summary>
        public const float PulseKick = 0.10f;

        // ── Motion ────────────────────────────────────────────────────────────────────

        /// <summary>Exponential follow rate for the drawn heading. Snapping straight to the
        /// cursor makes the indicator jitter with the mouse; too slow and it lags the shot.</summary>
        public const float TurnResponse = 22f;

        // ── Depth ─────────────────────────────────────────────────────────────────────
        //
        // The rig sorts on the BODY's own layer, rebased on the feet every frame, and each
        // piece goes behind or in front of the body by the SIGN of the aim. It shipped first on
        // Overhead — above every painted layer, which made "always visible" literally true —
        // and the first thing the player reported was that aiming north drew the tip over
        // their own legs. A ground decal north of the feet is farther from the camera than the
        // body standing on it, and the only layer that can say so is the body's. Never
        // SortingConfig.Z_SKY as an order: it is a Z DEPTH, and passing it as a sortingOrder is
        // what buried the old chevron under wall tops, decorations and every VFX in the game.

        /// <summary>Sorting-order distance from the body in either direction. Y-sort moves 100
        /// order units per world unit, so a frame of running (~0.1 u) is ~10 units — this must
        /// not be crossed by that, and must stay small enough that an NPC a step away still
        /// interleaves with the rig correctly.</summary>
        public const int DepthGap = 12;

        /// <summary>Sine of the aim heading above which the aim pieces go BEHIND the body.
        /// A small dead band, so a level aim stays in front: beside the body the tip barely
        /// overlaps it, and in front is where its outline survives.</summary>
        public const float BehindAimSine = 0.15f;

        /// <summary>Order of each piece within the rig, relative to its base. The pool is
        /// always behind — light on the ground under the boots, never over them.</summary>
        public const int SubOrderAura = 0;
        public const int SubOrderTip = 1;
    }
}
