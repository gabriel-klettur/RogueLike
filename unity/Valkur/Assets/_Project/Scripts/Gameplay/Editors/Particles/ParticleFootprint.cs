using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// The area a placed particle preset actually COVERS on screen, in world units, as an
    /// axis-aligned circle or rectangle offset from the emitter's position.
    ///
    /// Not the emission shape. Where particles are BORN is a fraction of where they end up:
    /// a leaf field emits from a 3 x 4 box and then drifts every leaf 1.1 units down over its
    /// two-second life, and each leaf is another half unit of quad on top of that — so an
    /// outline drawn on the spawn box alone leaves leaves falling outside it on three sides,
    /// which is exactly what it did. The footprint is therefore SWEPT: emission extent, plus
    /// everything the preset does to a particle before it dies.
    ///
    /// The emission half comes from <c>ParticleEmitter.ConfigureShape</c>, case for case; the
    /// motion half from the modules <c>ConfigureParticleSystem</c> writes — constant drift,
    /// scalar gravity, the initial throw, radial pull, noise and the particle's own size.
    /// If the two ever disagree the marker is lying about where the particles are, which is
    /// the state this type exists to end. Composites take the UNION over the root and every
    /// layer, because one placed instance spawns the whole stack.
    ///
    /// Rotation is deliberately dropped: a spawn box aimed by <c>directionDegrees</c> is
    /// drawn as its axis-aligned extent. The marker is a handle for finding and grabbing an
    /// emitter, not a physics volume, and an outline that spins with an authored heading is
    /// harder to hit than the box it stands for.
    /// </summary>
    public readonly struct ParticleFootprint
    {
        /// <summary>
        /// How much a footprint is INFLATED BY FOR PICKING, never for drawing. A dash puff
        /// emits from a 0.1-unit circle and a water fountain from 0.05 — a hair over two
        /// screen pixels at 16 PPU — so something has to make them clickable.
        ///
        /// It used to be a floor inside the struct, applied to every half-extent. That made
        /// the marker LIE at the small end: an author shrinking a field's width watched the
        /// box stop at 0.22 while the emission kept going down to 0.075, so the handle came
        /// off the cursor and the drawn area no longer described the effect. A box that
        /// reports a size the emitter is not using is exactly what this type exists to end,
        /// so the allowance moved to the one place that needs it — the hit test, which asks
        /// for <see cref="Inflated"/>.
        /// </summary>
        public const float MinHalfExtent = 0.22f;

        /// <summary>
        /// Largest half-extent a PREDICTED footprint may have. The sweep multiplies speed by
        /// lifespan and has to assume the worst case for both, and the catalog holds spell
        /// presets — a projectile trail at 16 u/s for 3 s sweeps 48 units, an outline larger
        /// than the screen and useless as a handle.
        ///
        /// It is deliberately NOT applied to a measurement. A clamped measurement is a marker
        /// that claims to bound particles it does not bound — <c>arcane_flame_emitter</c>
        /// really does spread 163 units across, and drawing 16 around it would be a lie the
        /// author cannot see. Predictions live for the frame or two before
        /// <see cref="OfLive"/> can measure the real thing, so clipping one costs nothing.
        /// </summary>
        public const float MaxHalfExtent = 8f;

        /// <summary>
        /// Extra fraction of a quad's half-size that a rotated particle reaches beyond its
        /// axis-aligned box. Unity builds <c>ParticleSystemRenderer.bounds</c> from position
        /// and size alone — per-particle rotation is not in it — so a spinning square pokes
        /// out by up to (sqrt2 - 1) / 2 of its size at 45 degrees. Measured across the
        /// catalog that is the only thing the renderer's own bounds miss, and it misses it by
        /// centimetres, but a marker that clips a leaf's corner is exactly what this type
        /// exists to prevent.
        /// </summary>
        private const float RotationSlack = 0.2072f;   // (sqrt(2) - 1) / 2

        /// <summary>
        /// Fixed pad on every MEASURED footprint, in world units. Unity's particle bounds are
        /// built from the state the renderer last saw, so a fast particle is already a
        /// fraction of a step past them — measured across the catalog the two fountain
        /// presets, whose droplets move at 4 u/s under 31 u/s^2 of gravity, sat up to 3.5 cm
        /// outside their own reported bounds. Under a pixel at 16 PPU, and the only thing
        /// standing between "covers the particles" and "covers them except when they are
        /// fast".
        /// </summary>
        private const float LiveSafetyPad = 0.05f;

        /// <summary>
        /// Seconds of travel added to the measured pad, at the preset's own top speed. Unity's
        /// bounds trail the simulation by a step or two, which is invisible for a drifting
        /// leaf and 20 cm for a fountain droplet at 4 u/s. Three frames at 60 Hz.
        /// </summary>
        private const float LiveLagSeconds = 0.05f;

        /// <summary>
        /// Safety margin added to a PREDICTED envelope: a fraction of its own size plus a
        /// fixed floor. Every term in the sweep is a model of a Unity module rather than the
        /// module itself — the noise module overshoots its authored strength slightly, sizes
        /// interpolate through curves, speeds are sampled per particle — and measured across
        /// the catalog those approximations left presets short by up to a tenth of a unit.
        /// A prediction is meant to be a BOUND; being a few percent generous costs nothing,
        /// being a centimetre short puts particles outside their own marker.
        /// </summary>
        /// <summary>
        /// How many multiples of `strength x lifetime` Unity's noise module can displace a
        /// particle. Measured at 3.67 across the catalog's noisy presets; four leaves margin.
        /// </summary>
        private const float NOISE_TRAVEL_FACTOR = 4f;

        private const float PredictionMargin = 0.12f;
        private const float PredictionMarginFloor = 0.06f;

        /// <summary>Fallback marker for an emitter whose preset cannot be resolved.</summary>
        public static readonly ParticleFootprint Default =
            new ParticleFootprint(false, Vector2.zero, 0.45f, 0.45f, clipped: false, predicted: true);

        public readonly bool IsRect;

        /// <summary>Offset of the covered area from the emitter's own position. Non-zero
        /// whenever the preset drifts its particles one way — a leaf field's box hangs below
        /// the emitter because that is where the leaves are.</summary>
        public readonly Vector2 Center;

        public readonly float HalfWidth;
        public readonly float HalfHeight;

        /// <summary>
        /// True when this footprint was PREDICTED from the preset rather than measured from
        /// the particles on screen. A prediction has to bound the worst case of every module
        /// it models, so it is generous by design — the outline treats the switch to a
        /// measurement as a hard cut rather than easing into it, and the extreme envelopes
        /// ignore predictions entirely: recording a bound as if it were an observation would
        /// have the "ever covered" box claim an area no particle ever reached.
        /// </summary>
        public readonly bool Predicted;

        /// <summary>
        /// True when this is a prediction that ran into <see cref="MaxHalfExtent"/> and was
        /// cut down to stay a usable handle. Such a footprint does NOT bound the effect —
        /// <c>water_fountain_small</c> throws water 20 units below its spout — and the
        /// coverage guard exempts it for exactly that reason. Measurements are never clipped.
        /// </summary>
        public readonly bool Clipped;

        /// <summary>Radius of a circular footprint; the larger half-extent of a rect.</summary>
        public float Radius => Mathf.Max(HalfWidth, HalfHeight);

        /// <summary>Covered area — the tie-break when several emitters sit under one click.</summary>
        public float Area => IsRect
            ? 4f * HalfWidth * HalfHeight
            : Mathf.PI * HalfWidth * HalfWidth;

        private ParticleFootprint(bool isRect, Vector2 center, float halfWidth, float halfHeight,
                                  bool clipped = false, bool predicted = false)
        {
            IsRect = isRect;
            Center = center;
            Clipped = clipped;
            Predicted = predicted;
            // Never capped and no longer floored: a marker states the size the emitter is
            // actually using. Capping one makes it claim a bound it does not hold (the cap
            // belongs to the prediction alone — see MaxHalfExtent), and flooring one makes it
            // overstate a small emitter, which is what put the drawn box off the cursor at the
            // bottom of a resize drag. Clicking a tiny emitter is handled by Inflated().
            HalfWidth = Mathf.Max(0f, halfWidth);
            HalfHeight = Mathf.Max(0f, halfHeight);
        }

        public static ParticleFootprint Circle(float radius)
            => new ParticleFootprint(false, Vector2.zero, radius, radius);

        public static ParticleFootprint Circle(Vector2 center, float radius)
            => new ParticleFootprint(false, center, radius, radius);

        public static ParticleFootprint Rect(float halfWidth, float halfHeight)
            => new ParticleFootprint(true, Vector2.zero, halfWidth, halfHeight);

        public static ParticleFootprint Rect(Vector2 center, float halfWidth, float halfHeight)
            => new ParticleFootprint(true, center, halfWidth, halfHeight);

        /// <summary>Rect from an explicit min/max envelope.</summary>
        private static ParticleFootprint FromBounds(bool isRect, Vector2 min, Vector2 max,
                                                    bool clipped = false, bool predicted = false)
        {
            Vector2 center = (min + max) * 0.5f;
            return new ParticleFootprint(isRect, center,
                                         (max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f,
                                         clipped, predicted);
        }

        public Vector2 Min => Center - new Vector2(HalfWidth, HalfHeight);
        public Vector2 Max => Center + new Vector2(HalfWidth, HalfHeight);

        /// <summary>
        /// The same box grown by <paramref name="pad"/> on every side. For hit testing: a
        /// marker is drawn at the size the emitter uses, and CLICKED with an allowance, so a
        /// two-pixel emitter is still selectable without its outline claiming to be bigger
        /// than it is.
        /// </summary>
        public ParticleFootprint Inflated(float pad)
        {
            if (pad <= 0f) return this;
            return new ParticleFootprint(IsRect, Center, HalfWidth + pad, HalfHeight + pad,
                                         Clipped, Predicted);
        }

        /// <summary>True when <paramref name="offset"/> (a world-space delta from the
        /// emitter's position) lies inside this footprint.</summary>
        public bool Contains(Vector2 offset)
        {
            Vector2 local = offset - Center;

            if (IsRect)
                return Mathf.Abs(local.x) <= HalfWidth && Mathf.Abs(local.y) <= HalfHeight;

            return local.sqrMagnitude <= HalfWidth * HalfWidth;
        }

        /// <summary>
        /// The union of two footprints: the smallest marker that covers both. A circle only
        /// survives against a concentric circle — mixing a box in, or two discs sitting at
        /// different offsets, means the extents no longer agree on both axes, and a circle
        /// drawn round that claims area the emitter never touches.
        /// </summary>
        public ParticleFootprint Union(ParticleFootprint other)
        {
            Vector2 min = Vector2.Min(Min, other.Min);
            Vector2 max = Vector2.Max(Max, other.Max);

            bool clipped = Clipped || other.Clipped;
            bool predicted = Predicted || other.Predicted;
            bool circle = !IsRect && !other.IsRect && Center == other.Center && !clipped;
            if (circle)
                return new ParticleFootprint(false, Center,
                    Mathf.Max(HalfWidth, other.HalfWidth), Mathf.Max(HalfWidth, other.HalfWidth),
                    clipped, predicted);

            return FromBounds(true, min, max, clipped, predicted);
        }

        // ── Resolution from a preset ─────────────────────────────────────────────

        /// <summary>
        /// Footprint of a LIVE emitter, measured from the particles it currently has on
        /// screen rather than predicted from its preset.
        ///
        /// Preferred wherever an emitter is actually running, because the prediction can only
        /// assume the worst case and the worst case is rare: <c>startSpeed</c> is a random
        /// 0..speed per particle, so a preset whose sparks are authored at 2.6 u/s over a
        /// 1.6 s life reserves 4 units of travel that almost no particle takes. Measured
        /// against the running system that preset covers 6 units across; the arithmetic says
        /// 10. Where nothing has moved off a wide random range — a drifting leaf field — the
        /// two agree to within a few percent.
        ///
        /// Falls back to the analytic sweep when nothing is alive yet: a freshly placed
        /// emitter has no particles for a frame or two, and a marker that pops from nothing
        /// to full size reads as a bug.
        /// </summary>
        public static ParticleFootprint OfLive(ParticleEmitter emitter)
        {
            if (emitter == null) return Default;

            Bounds bounds;
            if (!emitter.TryGetLiveBounds(out bounds))
                return OfBlocks(emitter.EffectiveBlocks, emitter.ScaleMultiplier);

            // Unity's bounds ignore per-particle rotation, so a spinning quad reaches a little
            // past them. Padded by the largest quad in the stack rather than per particle:
            // the renderer does not report which particle set the edge.
            float pad = LiveSafetyPad
                      + RotationPadOfBlocks(emitter.EffectiveBlocks,
                                            Mathf.Max(0.01f, emitter.ScaleMultiplier));

            Vector3 origin = emitter.transform.position;
            var min = new Vector2(bounds.min.x - origin.x - pad, bounds.min.y - origin.y - pad);
            var max = new Vector2(bounds.max.x - origin.x + pad, bounds.max.y - origin.y + pad);
            return FromBounds(true, min, max);
        }

        /// <summary>
        /// Swept footprint over an explicit list of blocks — root first, then layers — which is
        /// what a placed emitter running its OWN configuration reports through
        /// <c>ParticleEmitter.EffectiveBlocks</c>.
        ///
        /// The preset-based entry points below are the same computation reading the asset. Both
        /// exist because both questions are asked: "how big is this emitter" (blocks, and the
        /// only honest answer once copy-on-place lets an instance diverge from its preset) and
        /// "how big would a fresh placement of this preset be" (asset).
        /// </summary>
        public static ParticleFootprint OfBlocks(IReadOnlyList<ParticleVfxParams> blocks, float scaleMultiplier)
            => FoldBlocks(blocks, scaleMultiplier, sweep: true);

        /// <summary>Emission-only footprint over an explicit list of blocks.</summary>
        public static ParticleFootprint OfEmissionBlocks(IReadOnlyList<ParticleVfxParams> blocks,
                                                         float scaleMultiplier)
            => FoldBlocks(blocks, scaleMultiplier, sweep: false);

        /// <summary>Raw emission half-extents over an explicit list of blocks, unpadded — the
        /// base a resize drag takes its ratios against.</summary>
        public static Vector2 EmissionHalfExtentsOfBlocks(IReadOnlyList<ParticleVfxParams> blocks,
                                                          float scaleMultiplier)
        {
            if (blocks == null || blocks.Count == 0) return new Vector2(0.45f, 0.45f);

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            float hx = 0f, hy = 0f;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] == null) continue;
                bool circle;
                float bx, by;
                EmissionExtent(blocks[i], scale, out circle, out bx, out by);
                hx = Mathf.Max(hx, bx);
                hy = Mathf.Max(hy, by);
            }

            return new Vector2(hx, hy);
        }

        /// <summary>Largest lifetime travel over an explicit list of blocks.</summary>
        public static float LifetimeTravelOfBlocks(IReadOnlyList<ParticleVfxParams> blocks,
                                                   float scaleMultiplier)
        {
            if (blocks == null) return 0f;

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            float travel = 0f;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i] != null) travel = Mathf.Max(travel, LifetimeTravel(blocks[i], scale));
            return travel;
        }

        private static ParticleFootprint FoldBlocks(IReadOnlyList<ParticleVfxParams> blocks,
                                                    float scaleMultiplier, bool sweep)
        {
            if (blocks == null || blocks.Count == 0) return Default;

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            ParticleFootprint result = Default;
            bool any = false;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] == null) continue;
                var one = sweep ? OfParams(blocks[i], scale) : EmissionOf(blocks[i], scale);
                result = any ? result.Union(one) : one;
                any = true;
            }

            return any ? result : Default;
        }

        /// <summary>
        /// Footprint of a whole placed instance: the root's own swept area unioned with every
        /// layer's, at the instance's scale multiplier.
        /// </summary>
        public static ParticleFootprint Of(ParticlePresetDefinition preset, float scaleMultiplier)
            => Of(preset, scaleMultiplier, ParticleInstanceOverrides.None);

        /// <summary>
        /// Same, for a placement carrying its own size overrides. The overrides go through
        /// <see cref="ParticleOverrideApplier"/> — the very code the emitter uses to build its
        /// systems — so a resized instance's marker cannot drift from the effect inside it.
        /// </summary>
        public static ParticleFootprint Of(ParticlePresetDefinition preset, float scaleMultiplier,
                                           ParticleInstanceOverrides overrides)
            => Fold(preset, scaleMultiplier, overrides, sweep: true);

        /// <summary>
        /// Where this placement's particles are BORN — emission shapes only, with none of the
        /// travel that follows. This is the inner of the two precalculated boxes: the area the
        /// effect is anchored to, which is also the one an author resizes directly, because
        /// spawn width and height are real fields and reach is a consequence.
        /// </summary>
        public static ParticleFootprint OfEmission(ParticlePresetDefinition preset, float scaleMultiplier)
            => OfEmission(preset, scaleMultiplier, ParticleInstanceOverrides.None);

        /// <summary>Emission-only footprint for a placement with size overrides.</summary>
        public static ParticleFootprint OfEmission(ParticlePresetDefinition preset, float scaleMultiplier,
                                                   ParticleInstanceOverrides overrides)
            => Fold(preset, scaleMultiplier, overrides, sweep: false);

        /// <summary>
        /// Root unioned with every valid layer, either swept (the full reach) or emission-only.
        /// Both boxes are computed the same way from the same blocks so they can never
        /// disagree about which layers count.
        /// </summary>
        private static ParticleFootprint Fold(ParticlePresetDefinition preset, float scaleMultiplier,
                                              ParticleInstanceOverrides overrides, bool sweep)
        {
            if (preset == null || preset.vfx == null) return Default;

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            var rootBlock = ParticleOverrideApplier.Apply(preset.vfx, overrides);
            ParticleFootprint result = sweep ? OfParams(rootBlock, scale) : EmissionOf(rootBlock, scale);

            if (preset.layers != null)
            {
                for (int i = 0; i < preset.layers.Count; i++)
                {
                    var layer = preset.layers[i];
                    // Same skips the emitter makes when it builds the stack: a null slot and
                    // a self-reference render nothing, so neither adds area.
                    if (layer == null || layer == preset || layer.vfx == null) continue;

                    var block = ParticleOverrideApplier.Apply(layer.vfx, overrides);
                    result = result.Union(sweep ? OfParams(block, scale) : EmissionOf(block, scale));
                }
            }

            return result;
        }

        /// <summary>
        /// Raw emission half-extents of a whole placement, in world units, WITHOUT the
        /// minimum-handle floor. The floor exists so a point-like emitter stays clickable; it
        /// is the wrong number to divide by when converting a dragged edge into a ratio,
        /// because a 0.05-unit leaf strip would be measured against 0.22 and resize four times
        /// too slowly.
        /// </summary>
        public static Vector2 EmissionHalfExtents(ParticlePresetDefinition preset, float scaleMultiplier,
                                                  ParticleInstanceOverrides overrides)
        {
            if (preset == null || preset.vfx == null) return new Vector2(0.45f, 0.45f);

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            float hx, hy;
            bool circle;
            EmissionExtent(ParticleOverrideApplier.Apply(preset.vfx, overrides), scale, out circle, out hx, out hy);

            if (preset.layers != null)
            {
                for (int i = 0; i < preset.layers.Count; i++)
                {
                    var layer = preset.layers[i];
                    if (layer == null || layer == preset || layer.vfx == null) continue;

                    float lx, ly;
                    bool lc;
                    EmissionExtent(ParticleOverrideApplier.Apply(layer.vfx, overrides), scale, out lc, out lx, out ly);
                    hx = Mathf.Max(hx, lx);
                    hy = Mathf.Max(hy, ly);
                }
            }

            return new Vector2(hx, hy);
        }

        /// <summary>
        /// A CONSERVATIVE LOWER BOUND on how far a particle of this block gets from where it
        /// was born, over its whole life, in world units. Motion terms only — no emission
        /// extent, no quad size.
        ///
        /// Deliberately an under-estimate, and used for exactly one thing: deciding whether a
        /// resize has stopped an effect moving (ParticleBoundsHandles.ClampToVisibleMotion).
        /// Erring low makes the guard stop a drag slightly early, which costs an author a
        /// little range; erring high lets them drag an effect into stillness, which is the bug
        /// this whole mechanism exists to prevent. The footprint's own sweep is the opposite —
        /// there the worst case is what has to be reserved — so the two are separate functions
        /// on purpose.
        ///
        /// The largest single term rather than their sum: motion reads on screen as whatever
        /// dominates it. Each term is bounded by what the geometry actually allows:
        ///
        ///  • The initial throw is randomised 0..speed, so the TYPICAL particle takes half.
        ///  • Inward radial pull cannot carry a particle further than the radius it started
        ///    at — it reaches the centre and stays there. Measured on the portal's inflow,
        ///    crediting the full 0.3 u where the geometry allowed 0.06 was what let a squashed
        ///    emitter pass a check it visibly failed.
        ///  • An orbit sweeps an arc at the radius it is turning around, HALVED because that
        ///    radius is not held: the same inward pull that makes an inflow an inflow drags
        ///    the particle toward the centre, and the arc shrinks with it. Where there is no
        ///    inward pull the arc runs for the whole life.
        /// </summary>
        public static float LifetimeTravel(ParticleVfxParams v, float scale)
        {
            if (v == null) return 0f;

            float life = Mathf.Max(0.05f, v.lifespan);

            // startSpeed is a random 0..speed along the shape normal.
            float travel = 0.5f * v.speed * scale * life;

            travel = Mathf.Max(travel, v.useGravityVector
                ? v.gravityVector.magnitude * life
                : 0.5f * Mathf.Max(0f, v.gravity) * life * life);

            // The radius a particle is born at, and therefore the room an inward pull has.
            bool circle;
            float hx, hy;
            EmissionExtent(v, scale, out circle, out hx, out hy);
            float spawnRadius = Mathf.Max(hx, hy);

            float inward = v.radialSpeed < 0f ? -v.radialSpeed * scale : 0f;
            float radial = Mathf.Abs(v.radialSpeed) * scale * life;
            if (inward > 0f) radial = Mathf.Min(radial, spawnRadius);
            travel = Mathf.Max(travel, radial);

            if (Mathf.Abs(v.orbitalSpeedDegrees) > 0.01f)
            {
                // How long the particle still has a radius to orbit at.
                float orbiting = inward > 0f ? Mathf.Min(life, spawnRadius / inward) : life;
                float arc = Mathf.Abs(v.orbitalSpeedDegrees) * Mathf.Deg2Rad * orbiting
                            * (spawnRadius * 0.5f);
                travel = Mathf.Max(travel, arc);
            }

            if (v.noiseEnabled && v.noiseStrength > 0f)
                travel = Mathf.Max(travel, v.noiseStrength * scale);
            else if (string.Equals(v.kind, "falling_leaf", System.StringComparison.Ordinal))
                travel = Mathf.Max(travel, v.swayAmp * scale);

            return travel * TRAVEL_SAFETY;
        }

        /// <summary>
        /// What the terms above are multiplied by to make the estimate a bound rather than a
        /// guess. Measured against the running systems over 294 samples of the catalog, the
        /// raw terms came out HIGHER than the distance particles actually covered in 51 of
        /// them, by as much as a factor of two — a fountain thrown upward under gravity ends
        /// its life closer to the spout than the length of the path it flew, drag damps a
        /// trail below its authored speed, and the mean particle is slower than the typical
        /// one this models. Whatever the mechanism, an over-estimate here lets a drag stop an
        /// effect while the guard believes it is still moving, so the whole estimate is
        /// discounted past the worst case observed. Set to 0.40 rather than the 0.45 the first
        /// measurement suggested: at 0.45 a noisy pollen variant came within 1% of the bound,
        /// and a bound that a random process can graze is not a bound.
        /// </summary>
        private const float TRAVEL_SAFETY = 0.40f;

        /// <summary>Largest lifetime travel across a whole placement — root and every layer.</summary>
        public static float LifetimeTravel(ParticlePresetDefinition preset, float scaleMultiplier,
                                           ParticleInstanceOverrides overrides)
        {
            if (preset == null || preset.vfx == null) return 0f;

            float scale = Mathf.Max(0.01f, scaleMultiplier);
            float travel = LifetimeTravel(ParticleOverrideApplier.Apply(preset.vfx, overrides), scale);

            if (preset.layers != null)
            {
                for (int i = 0; i < preset.layers.Count; i++)
                {
                    var layer = preset.layers[i];
                    if (layer == null || layer == preset || layer.vfx == null) continue;
                    travel = Mathf.Max(travel,
                        LifetimeTravel(ParticleOverrideApplier.Apply(layer.vfx, overrides), scale));
                }
            }

            return travel;
        }

        /// <summary>Emission extent of one block as a footprint, with no travel folded in.</summary>
        public static ParticleFootprint EmissionOf(ParticleVfxParams v, float scale)
        {
            if (v == null) return Default;

            bool isCircle;
            float hx, hy;
            EmissionExtent(v, scale, out isCircle, out hx, out hy);

            return isCircle
                ? new ParticleFootprint(false, Vector2.zero, hx, hy, false, predicted: true)
                : new ParticleFootprint(true, Vector2.zero, hx, hy, false, predicted: true);
        }

        /// <summary>
        /// Swept footprint of one <see cref="ParticleVfxParams"/> block: where it emits, plus
        /// where a particle can travel before it dies, plus the particle's own size.
        /// </summary>
        public static ParticleFootprint OfParams(ParticleVfxParams v, float scale)
        {
            if (v == null) return Default;

            bool emitsAsCircle;
            float hx, hy;
            EmissionExtent(v, scale, out emitsAsCircle, out hx, out hy);

            float life = Mathf.Max(0.05f, v.lifespan);

            // ---- Isotropic growth: reaches every direction equally ----

            // startSpeed is randomised 0..speed along the shape's own normal, so the FASTEST
            // particle travels this far outward. The worst case is the right one to reserve
            // here even though most particles fall short of it: this prediction stands only
            // for the frame or two before OfLive can measure the real thing, so over-covering
            // costs a briefly generous outline while under-covering means particles outside
            // their own marker — which, measured across the catalog, is what halving this
            // did to 34 presets.
            float grow = v.speed * scale * life;

            // Radial pull only widens the area when it pushes OUT. Negative pull draws
            // particles toward the centre, which never leaves the emission extent.
            grow += Mathf.Max(0f, v.radialSpeed) * scale * life;

            // Turbulence is not a fixed offset, and it is not a small one. Unity's noise module
            // pushes a particle along a field that scrolls, so its strength behaves like a
            // VELOCITY and the displacement grows with the lifetime: measured over the 44 noisy
            // presets in the catalog, with drift and throw disabled so only the module was
            // moving anything, particles ended up between 0.1 and 3.67 times `strength x life`
            // from where they started — the pollen haze wandering 4.4 units on an authored
            // strength of 0.22. Bounded at four times that product rather than by
            // reimplementing the module's integration: this term is a BOUND, not a simulation.
            //
            // An earlier shape — strength x (5 + scroll x life) — was fitted to two presets and
            // under-reserved the seven-second hazes by more than a unit, which is how they kept
            // escaping their own marker in the catalog-wide guard.
            float noiseTravel = NOISE_TRAVEL_FACTOR * scale * life;
            grow += v.noiseEnabled && v.noiseStrength > 0f
                ? v.noiseStrength * noiseTravel
                : (string.Equals(v.kind, "falling_leaf", System.StringComparison.Ordinal)
                    ? v.swayAmp * noiseTravel
                    : 0f);

            // The quad itself. sizeMin/sizeMax are the HEIGHT; sizeAspect scales the width.
            float peak = PeakSizeMultiplier(v);
            float halfH = 0.5f * v.sizeMax * scale * peak;
            float halfW = halfH * (v.sizeAspect > 0f ? v.sizeAspect : 1f);

            // A quad that spins reaches its own diagonal at 45 degrees.
            if (Rotates(v))
            {
                float diagonal = Mathf.Sqrt((halfW * halfW) + (halfH * halfH));
                halfW = halfH = diagonal;
            }

            // ---- Directional travel: shifts the area rather than growing it both ways ----

            Vector2 travel = v.useGravityVector
                ? v.gravityVector * life
                // Scalar gravity is written as main.gravityModifier and pulls straight down:
                // s = 1/2 a t^2, with a = gravity (the modifier is gravity / 9.81 against
                // Unity's -9.81 g).
                : new Vector2(0f, -0.5f * Mathf.Max(0f, v.gravity) * life * life);

            // An orbit turns any emission shape into the disc it sweeps, so a box emitter
            // with orbital velocity covers its own diagonal in every direction.
            if (Mathf.Abs(v.orbitalSpeedDegrees) > 0.01f)
            {
                float sweptRadius = Mathf.Sqrt((hx * hx) + (hy * hy));
                hx = hy = sweptRadius;
                emitsAsCircle = true;
            }

            Vector2 min = new Vector2(
                -hx - grow - halfW + Mathf.Min(0f, travel.x),
                -hy - grow - halfH + Mathf.Min(0f, travel.y));
            Vector2 max = new Vector2(
                 hx + grow + halfW + Mathf.Max(0f, travel.x),
                 hy + grow + halfH + Mathf.Max(0f, travel.y));

            // A circle survives only while nothing has broken its symmetry.
            bool stillACircle = emitsAsCircle
                && travel == Vector2.zero
                && Mathf.Abs((max.x - min.x) - (max.y - min.y)) < 1e-4f;

            // Safety margin, applied before the cap so a clipped prediction is not padded back
            // over the limit it was just cut to.
            Vector2 margin = new Vector2(
                Mathf.Max(PredictionMarginFloor, (max.x - min.x) * 0.5f * PredictionMargin),
                Mathf.Max(PredictionMarginFloor, (max.y - min.y) * 0.5f * PredictionMargin));
            min -= margin;
            max += margin;

            // The cap is applied HERE and nowhere else: a prediction may be clipped to stay a
            // usable handle, a measurement may not (see MaxHalfExtent). The flag travels with
            // it so a clipped footprint can never be mistaken for a bound that holds.
            var capMin = new Vector2(-MaxHalfExtent, -MaxHalfExtent);
            var capMax = new Vector2(MaxHalfExtent, MaxHalfExtent);
            bool clipped = min.x < capMin.x || min.y < capMin.y || max.x > capMax.x || max.y > capMax.y;
            min = Vector2.Max(min, capMin);
            max = Vector2.Min(max, capMax);

            if (stillACircle && !clipped)
                return new ParticleFootprint(false, Vector2.zero,
                    (max.x - min.x) * 0.5f, (max.x - min.x) * 0.5f, false, predicted: true);

            return FromBounds(true, min, max, clipped, predicted: true);
        }

        /// <summary>True when the preset spins its quads, either at birth or over life.</summary>
        private static bool Rotates(ParticleVfxParams v)
            => Mathf.Abs(v.startRotationJitterDegrees) > 0.01f || Mathf.Abs(v.rotationSpeedDegrees) > 0.01f;

        /// <summary>
        /// How far a stack's particles can sit outside the bounds Unity reports for them:
        /// the diagonal of the largest rotating quad, plus the distance the fastest particle
        /// covers while those bounds catch up.
        /// </summary>
        /// <summary>Rotation and lag pad over an explicit list of blocks.</summary>
        private static float RotationPadOfBlocks(IReadOnlyList<ParticleVfxParams> blocks, float scale)
        {
            if (blocks == null) return 0f;

            float pad = 0f;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i] != null) pad = Mathf.Max(pad, PadFor(blocks[i], scale));
            return pad;
        }

        private static float RotationPad(ParticlePresetDefinition preset, float scale,
                                         ParticleInstanceOverrides overrides)
        {
            if (preset == null || preset.vfx == null) return 0f;

            float pad = PadFor(ParticleOverrideApplier.Apply(preset.vfx, overrides), scale);
            if (preset.layers != null)
            {
                for (int i = 0; i < preset.layers.Count; i++)
                {
                    var layer = preset.layers[i];
                    if (layer == null || layer == preset || layer.vfx == null) continue;
                    pad = Mathf.Max(pad, PadFor(ParticleOverrideApplier.Apply(layer.vfx, overrides), scale));
                }
            }
            return pad;
        }

        private static float PadFor(ParticleVfxParams v, float scale)
        {
            // How far this preset's fastest particle travels while the reported bounds catch
            // up. Applies whether or not the quads rotate.
            float lag = v.speed * scale * LiveLagSeconds;

            if (!Rotates(v)) return lag;

            float peak = PeakSizeMultiplier(v);
            float height = v.sizeMax * scale * peak;
            float width = height * (v.sizeAspect > 0f ? v.sizeAspect : 1f);
            return lag + (RotationSlack * Mathf.Max(width, height));
        }

        /// <summary>
        /// Where the preset emits from, before anything moves — <c>ConfigureShape</c>'s switch
        /// and its two authored overrides, as half-extents.
        /// </summary>
        private static void EmissionExtent(ParticleVfxParams v, float scale,
                                           out bool isCircle, out float halfWidth, out float halfHeight)
        {
            isCircle = true;

            // Authored spawn box wins over the kind, exactly as in ConfigureShape.
            if (v.spawnWidth > 0f || v.spawnHeight > 0f)
            {
                isCircle = false;
                halfWidth = Mathf.Max(0.01f, v.spawnWidth) * 0.5f * scale;
                halfHeight = Mathf.Max(0.01f, v.spawnHeight) * 0.5f * scale;
                return;
            }

            // Heading without an area is a cone whose base is `dispersion` wide; the throw
            // along it is speed, which the sweep above already accounts for.
            if (v.directionDegrees >= 0f)
            {
                halfWidth = halfHeight = Mathf.Max(0.02f, v.dispersion) * scale;
                return;
            }

            float r;
            switch (v.kind ?? "")
            {
                case "aura":
                case "healing_aura":       r = v.radius * scale; break;
                case "portal":             r = (v.outerRadius > 0f ? v.outerRadius : v.radius) * scale; break;
                case "smoke":
                case "smoke_emitter":      r = (v.dispersion > 0f ? v.dispersion : 0.15f) * scale; break;
                case "arcane_flame":       r = 0.2f * scale; break;
                case "slash":              r = 0.2f * scale; break;
                case "dash":               r = 0.1f * scale; break;
                case "explosion":
                case "smoke_burst":
                case "firework":           r = 0.1f * scale; break;
                case "water_fountain":     r = 0.05f * scale; break;

                // Both strip kinds scale on BOTH axes, the way ConfigureShape builds them: their
                // height used to be a flat 0.1 there and here, so a placed instance at scale 2
                // emitted from a strip twice as wide and exactly as tall — and its marker then
                // clipped the particles by the difference.
                case "falling_leaf":
                    // Box 2 x 0.1: a wide, hair-thin strip the particles fall out of.
                    isCircle = false;
                    halfWidth = 1f * scale;
                    halfHeight = 0.05f * scale;
                    return;

                case "water_flow":
                    isCircle = false;
                    halfWidth = 1.5f * scale;
                    halfHeight = 0.05f * scale;
                    return;

                case "lightning":
                    // No ParticleSystem at all — the bolt is a LineRenderer that wanders up
                    // to lightningOffset either side of its span.
                    r = Mathf.Max(v.radius, v.lightningOffset) * scale;
                    break;

                default:                   r = 0.15f * scale; break;
            }

            halfWidth = halfHeight = r;
        }

        /// <summary>
        /// Largest value <c>sizeOverLifetime</c> ever multiplies the start size by. An empty
        /// curve leaves the module off (multiplier 1) for a looping emitter; a burst gets the
        /// engine's injected 0.3 → 1.0 → 0 expand-and-shrink, whose peak is also 1.
        ///
        /// SAMPLED, not read off the keys. <c>ParticleEmitter.BuildAnimationCurve</c> hands
        /// Unity a bare <c>new AnimationCurve(keys)</c>, which smooths the tangents — and a
        /// smoothed curve OVERSHOOTS between keys. A 0.5 → 1.0 → 0.6 size curve peaks above
        /// 1.0 somewhere in the middle, and taking the key value instead of the curve's is
        /// what left the pollen and fountain presets a third of a unit short of their own
        /// particles.
        /// </summary>
        private static float PeakSizeMultiplier(ParticleVfxParams v)
        {
            if (v.sizeOverLife == null || v.sizeOverLife.Length == 0) return 1f;

            var keyframes = new Keyframe[v.sizeOverLife.Length];
            for (int i = 0; i < v.sizeOverLife.Length; i++)
                keyframes[i] = new Keyframe(v.sizeOverLife[i].time, v.sizeOverLife[i].value);

            var curve = new AnimationCurve(keyframes);

            const int SAMPLES = 64;
            float peak = 0f;
            for (int i = 0; i <= SAMPLES; i++)
                peak = Mathf.Max(peak, curve.Evaluate(i / (float)SAMPLES));

            // A curve authored entirely at zero would erase the quad; treat it as absent
            // rather than reporting a footprint with no particle in it.
            return peak > 0f ? peak : 1f;
        }
    }
}
