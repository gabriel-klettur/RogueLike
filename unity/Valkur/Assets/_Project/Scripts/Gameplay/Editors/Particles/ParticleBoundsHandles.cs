using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>Which of a placed emitter's two authored boxes a handle belongs to.</summary>
    public enum ParticleBoundsBox
    {
        None = 0,

        /// <summary>Where particles are BORN. Backed by real fields — spawn width/height, or
        /// the emission radius — so dragging it edits the effect directly.</summary>
        Emission = 1,

        /// <summary>How far they GET. Backed by the motion terms as a whole (speed, drift,
        /// gravity, radial pull, turbulence), which is why it is driven by one reach ratio
        /// instead of by an edge position: several fields produce the same reach, and picking
        /// one of them to move would be an arbitrary choice the author cannot see.</summary>
        Reach = 2,
    }

    /// <summary>Side of a box a drag is acting on.</summary>
    public enum ParticleBoundsEdge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Bottom = 3,
        Top = 4,
    }

    /// <summary>Outcome of one drag step.</summary>
    public readonly struct ParticleBoundsDrag
    {
        /// <summary>Overrides the instance should now carry.</summary>
        public readonly ParticleInstanceOverrides Overrides;

        /// <summary>
        /// How far the emitter itself has to move, in world units. Non-zero when an edge is
        /// dragged with its opposite edge pinned: a box centred on the emitter cannot grow on
        /// one side alone unless the emitter slides half the growth the other way.
        /// </summary>
        public readonly Vector2 OriginDelta;

        /// <summary>False when the drag could not change anything — see
        /// <see cref="ParticleBoundsHandles.DragReachEdge"/> on a preset whose particles do
        /// not move.</summary>
        public readonly bool Changed;

        /// <summary>
        /// True when the drag stopped short of the cursor because going further would have
        /// frozen the effect: below a certain reach a leaf covers under a pixel over its whole
        /// life, so the field goes on spawning and dying without ever falling. The editor says
        /// so in the status line instead of letting the author wonder what broke.
        /// </summary>
        public readonly bool StoppedAtMotionFloor;

        public ParticleBoundsDrag(ParticleInstanceOverrides overrides, Vector2 originDelta, bool changed,
                                  bool stoppedAtMotionFloor = false)
        {
            Overrides = overrides;
            OriginDelta = originDelta;
            Changed = changed;
            StoppedAtMotionFloor = stoppedAtMotionFloor;
        }

        public static ParticleBoundsDrag Unchanged(ParticleInstanceOverrides current)
            => new ParticleBoundsDrag(current, Vector2.zero, false);
    }

    /// <summary>
    /// The geometry behind the F1 editor's resize handles: which edge the cursor is on, and
    /// what a drag of that edge does to the instance's <see cref="ParticleInstanceOverrides"/>.
    ///
    /// Deliberately a pure static class over value types — no scene, no input, no rendering.
    /// The interaction layer is a state machine that can only be exercised by driving a live
    /// editor; the arithmetic under it is where the mistakes actually live (a ratio taken
    /// against the wrong base, an edge that grows the wrong way, a reach solve that inverts on
    /// a preset with no motion), and all of it is reachable from an EditMode test at this
    /// boundary.
    ///
    /// Two rules run through everything here:
    ///
    ///  • RATIOS AGAINST THE PRESET, not absolute sizes. The instance stores "1.4x the preset's
    ///    width", so retuning the preset moves its instances with it instead of stranding them.
    ///  • THE BASE IS THE RAW EXTENT. Ratios are taken against
    ///    <c>ParticleFootprint.EmissionHalfExtents</c>, which is the emission area with no
    ///    picking allowance folded in — measuring a 0.05-unit leaf strip against a padded
    ///    number would make it resize several times too slowly.
    /// </summary>
    public static class ParticleBoundsHandles
    {
        /// <summary>
        /// Reach ratio used as the low sample when solving a reach drag. Not zero, because
        /// <see cref="ParticleInstanceOverrides.Sanitized"/> reads a non-positive ratio as
        /// "unset" and hands back 1 — the solve would then fit a line through two identical
        /// points.
        /// </summary>
        private const float ReachSolveLow = ParticleInstanceOverrides.MinRatio;

        /// <summary>
        /// Below this the two reach samples are indistinguishable and the preset simply has no
        /// motion to scale: an aura whose particles sit where they are born has the same reach
        /// box at every ratio, and dragging it would divide by ~0.
        /// </summary>
        private const float ReachSolveEpsilon = 1e-3f;

        /// <summary>
        /// Least distance, in world units, a particle must still cover over its WHOLE life for
        /// the effect to read as moving. Four art texels at 16 PPU.
        ///
        /// This is the floor under a reach drag, and it exists because of what a small reach
        /// actually does: the ratio multiplies every motion term, so at 0.05 a leaf field's
        /// drift falls from 0.55 u/s to 0.0275 — nine tenths of one pixel over a two-second
        /// life. The leaves go on spawning and dying exactly as before and never move, which
        /// reads as a broken emitter rather than as a small one. An author dragging the outer
        /// box inward is asking for a smaller effect, and past this point they stop getting
        /// one, so the drag stops here and the status line says which knob does what they
        /// meant (the emission box, or the instance's scale).
        ///
        /// Data written before this rule, or by hand, is not re-clamped: the floor guards the
        /// GESTURE, and silently rewriting a world file on load is a different and worse
        /// surprise.
        /// </summary>
        public const float MinVisibleLifetimeTravel = 0.25f;

        /// <summary>
        /// Second half of the same floor, as a FRACTION of what the preset was authored to
        /// travel. The absolute number above protects a slow effect from disappearing into
        /// sub-pixel motion; this one protects a fast one from being slowed past recognition —
        /// a projectile trail authored at ten units a life still covers four texels at a
        /// fortieth of its speed, and would read as a stalled effect long before the absolute
        /// floor caught it. An author may slow an effect to a fifth. Not to a stop.
        /// </summary>
        public const float MinVisibleTravelFraction = 0.2f;

        /// <summary>
        /// Below this much authored travel a preset is treated as static and its boxes are not
        /// guarded at all. Deliberately well under
        /// <see cref="MinVisibleLifetimeTravel"/>: the guard's job is to stop a MOVING effect
        /// being dragged into stillness, and a preset whose particles never go anywhere has no
        /// such transition to protect — clamping it would only cost the author range on a box
        /// that was never going to look frozen because it never looked moving.
        /// </summary>
        public const float MotionGuardThreshold = 0.10f;

        // ── Hit testing ──────────────────────────────────────────────────────────

        /// <summary>
        /// The edge of <paramref name="box"/> the cursor is within <paramref name="tolerance"/>
        /// of, or None. The cursor must also be near the box on the other axis, so the
        /// extension of an edge far outside the box is not a handle.
        /// </summary>
        public static ParticleBoundsEdge PickEdge(ParticleFootprint box, Vector2 origin,
                                                  Vector2 cursor, float tolerance)
        {
            Vector2 local = cursor - origin - box.Center;
            float hw = box.HalfWidth;
            float hh = box.HalfHeight;

            bool withinX = Mathf.Abs(local.x) <= hw + tolerance;
            bool withinY = Mathf.Abs(local.y) <= hh + tolerance;
            if (!withinX || !withinY) return ParticleBoundsEdge.None;

            float dLeft = Mathf.Abs(local.x + hw);
            float dRight = Mathf.Abs(local.x - hw);
            float dBottom = Mathf.Abs(local.y + hh);
            float dTop = Mathf.Abs(local.y - hh);

            float best = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
            if (best > tolerance) return ParticleBoundsEdge.None;

            if (best == dLeft) return ParticleBoundsEdge.Left;
            if (best == dRight) return ParticleBoundsEdge.Right;
            if (best == dBottom) return ParticleBoundsEdge.Bottom;
            return ParticleBoundsEdge.Top;
        }

        /// <summary>
        /// Which box's border the cursor is on when both are drawn. The EMISSION box wins a
        /// tie: it is the inner one and the one whose handles edit real fields, so an author
        /// reaching for it must not have the reach box — which can be many times larger and
        /// therefore passes under the cursor far more often — taken instead.
        /// </summary>
        public static ParticleBoundsBox PickBox(ParticleFootprint emission, ParticleFootprint reach,
                                                Vector2 origin, Vector2 cursor, float tolerance)
        {
            if (PickEdge(emission, origin, cursor, tolerance) != ParticleBoundsEdge.None)
                return ParticleBoundsBox.Emission;
            if (PickEdge(reach, origin, cursor, tolerance) != ParticleBoundsEdge.None)
                return ParticleBoundsBox.Reach;
            return ParticleBoundsBox.None;
        }

        /// <summary>True for the two edges that move along X.</summary>
        public static bool IsHorizontal(ParticleBoundsEdge edge)
            => edge == ParticleBoundsEdge.Left || edge == ParticleBoundsEdge.Right;

        // ── Dragging the emission box ────────────────────────────────────────────

        /// <summary>
        /// Resize the emission box by moving one edge to <paramref name="cursorWorld"/>.
        ///
        /// With <paramref name="symmetric"/> false the OPPOSITE edge is pinned, which is what
        /// dragging a rectangle's side means everywhere else in the editor — and since the box
        /// is centred on the emitter, holding that edge still means moving the emitter by half
        /// the growth, reported as <see cref="ParticleBoundsDrag.OriginDelta"/>. With it true
        /// both sides move and the emitter stays.
        /// </summary>
        public static ParticleBoundsDrag DragEmissionEdge(
            ParticlePresetDefinition preset, float scaleMultiplier, ParticleInstanceOverrides current,
            ParticleBoundsEdge edge, Vector2 origin, Vector2 cursorWorld, bool symmetric, float snap)
        {
            if (preset == null || edge == ParticleBoundsEdge.None)
                return ParticleBoundsDrag.Unchanged(current);

            return DragEmissionEdge(
                new ParticleBoundsSubject(preset, scaleMultiplier),
                current, edge, origin, cursorWorld, symmetric, snap);
        }

        /// <summary>
        /// The same drag against an explicit subject — the blocks a placed emitter is running,
        /// rather than the preset asset it was born from. Once an instance owns its
        /// configuration (copy-on-place) the asset is the wrong base: the two have been free to
        /// diverge since it was placed, and taking ratios against the asset would jump the box
        /// the first time an author dragged one that had.
        /// </summary>
        public static ParticleBoundsDrag DragEmissionEdge(
            ParticleBoundsSubject subject, ParticleInstanceOverrides current,
            ParticleBoundsEdge edge, Vector2 origin, Vector2 cursorWorld, bool symmetric, float snap)
        {
            if (!subject.IsValid || edge == ParticleBoundsEdge.None)
                return ParticleBoundsDrag.Unchanged(current);

            float scaleMultiplier = subject.Scale;
            Vector2 baseHalf = subject.EmissionHalfExtents(ParticleInstanceOverrides.None);
            Vector2 currentHalf = subject.EmissionHalfExtents(current);

            bool horizontal = IsHorizontal(edge);
            float baseExtent = horizontal ? baseHalf.x : baseHalf.y;
            if (baseExtent <= 1e-4f) return ParticleBoundsDrag.Unchanged(current);

            float half = horizontal ? currentHalf.x : currentHalf.y;
            float axisOrigin = horizontal ? origin.x : origin.y;
            float target = Snap(horizontal ? cursorWorld.x : cursorWorld.y, snap);

            // Which way the dragged edge faces, so both sides use one formula.
            float sign = (edge == ParticleBoundsEdge.Right || edge == ParticleBoundsEdge.Top) ? 1f : -1f;

            float newHalf;
            Vector2 originDelta = Vector2.zero;

            if (symmetric)
            {
                newHalf = Mathf.Abs(target - axisOrigin);
            }
            else
            {
                float opposite = axisOrigin - (sign * half);       // the pinned edge, in world

                // Dragged past the pinned edge, the box would turn inside out: |target -
                // opposite| keeps growing on the far side, so pulling the right edge left
                // across the left one made the field GROW leftward and the emitter jump. The
                // gesture stops at the smallest legal size instead, against the edge it is
                // pinned to.
                float minHalf = baseExtent * ParticleInstanceOverrides.MinRatio;
                float limit = opposite + (sign * 2f * minHalf);
                target = sign > 0f ? Mathf.Max(target, limit) : Mathf.Min(target, limit);

                newHalf = Mathf.Abs(target - opposite) * 0.5f;

                float newCentre = (target + opposite) * 0.5f;
                float delta = newCentre - axisOrigin;
                originDelta = horizontal ? new Vector2(delta, 0f) : new Vector2(0f, delta);
            }

            float wanted = newHalf / baseExtent;
            float ratio = Mathf.Clamp(wanted, ParticleInstanceOverrides.MinRatio,
                                              ParticleInstanceOverrides.MaxRatio);

            // A clamped ratio means the edge did not land where the cursor is, so the emitter
            // must not slide the full distance either — the box would detach from the cursor
            // and keep walking as the author kept dragging. Compared with a RELATIVE epsilon:
            // Mathf.Approximately is scaled to 1.0, so at the bottom of the range it called
            // 0.05 and 0.013 equal and the emitter crept sideways on every frame of a drag
            // that was already pinned.
            if (!symmetric && Mathf.Abs(ratio - wanted) > 1e-4f * Mathf.Max(1f, Mathf.Abs(wanted)))
                originDelta = Vector2.zero;

            var next = horizontal
                ? new ParticleInstanceOverrides(ratio, current.spawnScaleY, current.reachScale)
                : new ParticleInstanceOverrides(current.spawnScaleX, ratio, current.reachScale);

            // An emission box can freeze an effect too: orbital motion covers ground in
            // proportion to the radius it turns around. See ClampToVisibleMotion.
            bool stoppedAtMotion;
            next = ClampToVisibleMotion(subject, current, next.Sanitized(), out stoppedAtMotion);

            // Same rule as a clamped ratio: if the size stopped following the cursor, the
            // emitter has to stop sliding with it.
            if (stoppedAtMotion) originDelta = Vector2.zero;

            return new ParticleBoundsDrag(next, originDelta, true, stoppedAtMotion);
        }

        // ── Dragging the reach box ───────────────────────────────────────────────

        /// <summary>
        /// Resize the reach box by moving one edge to <paramref name="cursorWorld"/>, solving
        /// for the reach ratio that puts it there.
        ///
        /// Solved rather than derived: the edge position is the sum of an emission extent, a
        /// throw, a drift, a gravity drop, a turbulence allowance and half a quad, and every
        /// one of those terms is LINEAR in the reach ratio. Sampling the footprint at two
        /// ratios and fitting a line through them therefore inverts the whole thing exactly —
        /// and keeps doing so when a term is added, which hand-differentiating this expression
        /// would not.
        ///
        /// Both sides of the box move: reach grows outward in every direction (and along the
        /// drift), so there is no opposite edge to pin. The emitter never moves.
        /// </summary>
        public static ParticleBoundsDrag DragReachEdge(
            ParticlePresetDefinition preset, float scaleMultiplier, ParticleInstanceOverrides current,
            ParticleBoundsEdge edge, Vector2 origin, Vector2 cursorWorld, float snap)
        {
            if (preset == null || edge == ParticleBoundsEdge.None)
                return ParticleBoundsDrag.Unchanged(current);

            return DragReachEdge(new ParticleBoundsSubject(preset, scaleMultiplier),
                                 current, edge, origin, cursorWorld, snap);
        }

        /// <summary>The reach drag against an explicit subject. See the emission sibling.</summary>
        public static ParticleBoundsDrag DragReachEdge(
            ParticleBoundsSubject subject, ParticleInstanceOverrides current,
            ParticleBoundsEdge edge, Vector2 origin, Vector2 cursorWorld, float snap)
        {
            if (!subject.IsValid || edge == ParticleBoundsEdge.None)
                return ParticleBoundsDrag.Unchanged(current);

            float low = subject.EdgePosition(WithReach(current, ReachSolveLow), edge);
            float high = subject.EdgePosition(WithReach(current, 1f), edge);

            float slope = high - low;                       // per unit of reach ratio
            if (Mathf.Abs(slope) < ReachSolveEpsilon)
                return ParticleBoundsDrag.Unchanged(current);

            bool horizontal = IsHorizontal(edge);
            float axisOrigin = horizontal ? origin.x : origin.y;
            float target = Snap(horizontal ? cursorWorld.x : cursorWorld.y, snap) - axisOrigin;

            // low was sampled at ReachSolveLow, not at 0, so the line is anchored there.
            float ratio = ReachSolveLow + ((target - low) / slope) * (1f - ReachSolveLow);

            ratio = Mathf.Clamp(ratio, ParticleInstanceOverrides.MinRatio,
                                       ParticleInstanceOverrides.MaxRatio);

            var next = new ParticleInstanceOverrides(current.spawnScaleX, current.spawnScaleY, ratio);

            // Never drag an effect into stillness. See ClampToVisibleMotion.
            bool stopped;
            next = ClampToVisibleMotion(subject, current, next.Sanitized(), out stopped);

            return new ParticleBoundsDrag(next, Vector2.zero, true, stopped);
        }

        /// <summary>
        /// Smallest reach ratio that still leaves this preset visibly moving, given how far it
        /// travels at ratio 1. Returns the absolute minimum for a preset that barely moves to
        /// begin with — there is no stillness to protect it from, and clamping it would make
        /// its reach box unresizable for no reason.
        /// </summary>
        public static float MinVisibleReachRatio(ParticlePresetDefinition preset, float scaleMultiplier,
                                                 ParticleInstanceOverrides current)
            => MinVisibleReachRatio(new ParticleBoundsSubject(preset, scaleMultiplier), current);

        /// <summary>The same against an explicit subject.</summary>
        public static float MinVisibleReachRatio(ParticleBoundsSubject subject,
                                                 ParticleInstanceOverrides current)
        {
            float atOne = subject.LifetimeTravel(WithReach(current, 1f));

            if (atOne <= MinVisibleLifetimeTravel) return ParticleInstanceOverrides.MinRatio;

            float floor = Mathf.Max(MinVisibleLifetimeTravel / atOne, MinVisibleTravelFraction);
            return Mathf.Clamp(floor, ParticleInstanceOverrides.MinRatio, 1f);
        }

        /// <summary>
        /// Distance this preset's particles must still cover over a lifetime for the effect to
        /// read as moving: four art texels, or a fifth of what it was authored to travel,
        /// whichever is larger. Zero for a preset that never moves much anyway.
        /// </summary>
        public static float VisibleTravelFloor(ParticlePresetDefinition preset, float scaleMultiplier)
            => VisibleTravelFloor(new ParticleBoundsSubject(preset, scaleMultiplier));

        /// <summary>The same against an explicit subject.</summary>
        public static float VisibleTravelFloor(ParticleBoundsSubject subject)
        {
            float authored = subject.LifetimeTravel(ParticleInstanceOverrides.None);

            // Nothing to protect: this preset's particles barely leave the spot they are born
            // in even at full size, so a resize cannot take away a motion the author had.
            // Guarding it would only make its boxes needlessly unresizable.
            if (authored <= MotionGuardThreshold) return 0f;

            // Bounded by what the preset HAS: a preset authored just above the visible
            // threshold cannot be asked to keep more motion than it ever had.
            return Mathf.Max(authored * MinVisibleTravelFraction,
                             Mathf.Min(authored, MinVisibleLifetimeTravel));
        }

        /// <summary>
        /// Holds a drag back from the point where the effect stops moving, whichever box is
        /// being dragged.
        ///
        /// The reach ratio is not the only way to freeze an effect: motion driven by an ORBIT
        /// covers ground proportional to the emission radius, so shrinking the EMISSION box of
        /// a preset like the portal's inflow slows its particles just as surely — measured, a
        /// twentieth of the radius took them from 0.28 u/s to a quarter of a pixel per second.
        /// Rather than special-case each term, the guard asks the one question that matters —
        /// how far does a particle get over its life — and walks the drag back until the answer
        /// is still visible.
        ///
        /// Bisected rather than solved: travel is monotonic in the ratios but is a max over
        /// several terms, some of them products (an orbit's arc is angular rate x radius), and
        /// a closed form would have to be re-derived every time a term is added. Twenty
        /// halvings land within a thousandth of the boundary and cost nothing on a drag.
        ///
        /// A placement that is ALREADY below the floor — old data, or a hand-edited file — is
        /// not dragged up to it. The rule is that a gesture may not make an effect stiller,
        /// not that every instance must pass a check it never agreed to.
        /// </summary>
        private static ParticleInstanceOverrides ClampToVisibleMotion(
            ParticleBoundsSubject subject,
            ParticleInstanceOverrides current, ParticleInstanceOverrides candidate,
            out bool stopped)
        {
            stopped = false;

            float floor = VisibleTravelFloor(subject);
            if (floor <= 0f) return candidate;

            float wanted = subject.LifetimeTravel(candidate);
            if (wanted >= floor) return candidate;

            stopped = true;

            float have = subject.LifetimeTravel(current);
            if (have <= floor) return current;   // already stiller than the floor: hold, do not push

            // Walk back along the straight line from what the instance has to what the cursor
            // asked for, and keep the furthest point that still moves.
            float lo = 0f, hi = 1f;              // 0 = current, 1 = candidate
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * 0.5f;
                var probe = Lerp(current, candidate, mid);
                if (subject.LifetimeTravel(probe) >= floor) lo = mid;
                else hi = mid;
            }

            return Lerp(current, candidate, lo);
        }

        private static ParticleInstanceOverrides Lerp(ParticleInstanceOverrides a,
                                                      ParticleInstanceOverrides b, float t)
            => new ParticleInstanceOverrides(
                Mathf.Lerp(a.spawnScaleX, b.spawnScaleX, t),
                Mathf.Lerp(a.spawnScaleY, b.spawnScaleY, t),
                Mathf.Lerp(a.reachScale, b.reachScale, t)).Sanitized();

        /// <summary>
        /// Signed position of one edge of the reach box, relative to the emitter. Left and
        /// Bottom are negative, which is what makes the reach solve work on all four sides
        /// with one formula.
        /// </summary>
        public static float EdgePosition(ParticlePresetDefinition preset, float scaleMultiplier,
                                         ParticleInstanceOverrides overrides, ParticleBoundsEdge edge)
            => new ParticleBoundsSubject(preset, scaleMultiplier).EdgePosition(overrides, edge);

        /// <summary>Signed edge position of a footprint, shared by both subjects.</summary>
        internal static float EdgeOf(ParticleFootprint box, ParticleBoundsEdge edge)
        {
            switch (edge)
            {
                case ParticleBoundsEdge.Left:   return box.Min.x;
                case ParticleBoundsEdge.Right:  return box.Max.x;
                case ParticleBoundsEdge.Bottom: return box.Min.y;
                case ParticleBoundsEdge.Top:    return box.Max.y;
                default:                        return 0f;
            }
        }

        private static ParticleInstanceOverrides WithReach(ParticleInstanceOverrides o, float reach)
            => new ParticleInstanceOverrides(o.spawnScaleX, o.spawnScaleY, reach);

        /// <summary>
        /// Rounds a world coordinate to the authoring grid. The default step is one art texel
        /// at 16 PPU, so a dragged edge lands where the pixel grid can actually show it;
        /// passing 0 (the modifier the editor binds to Alt) drags free.
        /// </summary>
        public static float Snap(float value, float step)
            => step > 0f ? Mathf.Round(value / step) * step : value;
    }
}
