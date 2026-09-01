using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// <see cref="WeatherEffect"/> — the construction and geometry half: how a depth slice is
    /// created and pointed at a shared material, and how a slice is positioned, sized and
    /// timed against the live viewport and the live wind.
    ///
    /// Split out from the lifecycle half because these are the members a SUBCLASS spends its
    /// time in (they are what <c>BuildLayers</c> and <c>LayoutForViewport</c> are written
    /// against), while the other file is the frame loop nothing overrides.
    /// </summary>
    public abstract partial class WeatherEffect
    {
        // ── layer construction helpers ───────────────────────────────────────────────

        /// <summary>
        /// Create one depth slice as a child GameObject with its own ParticleSystem, stopped
        /// and cleared so the caller may configure it.
        ///
        /// Stopped matters: <c>AddComponent&lt;ParticleSystem&gt;</c> starts playing
        /// immediately (playOnAwake defaults true), and a playing system silently refuses a
        /// write to <c>main.duration</c> — the same trap every emitter builder in the project
        /// already works around.
        /// </summary>
        protected WeatherLayer CreateLayer(string name, float depth)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(transform, false);

            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode     = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = 0f;
            main.startSpeed      = 0f;

            var layer = new WeatherLayer(ps, depth);
            Layers.Add(layer);
            return layer;
        }

        /// <summary>
        /// Point a slice's renderer at a shared cached material and put it on the VFX sorting
        /// layer. VFX sits above every world sorting layer, which is what weather wants:
        /// precipitation is between the camera and the world, wall tops included.
        /// </summary>
        protected static void SetupRenderer(WeatherLayer layer, Texture2D texture, bool additive,
                                            int sortingOrder)
        {
            var r = layer.Renderer;
            r.sharedMaterial   = Valkur.Gameplay.VFX.ParticleMaterialCache.Get(texture, additive);
            r.sortingLayerName = SortingLayerExists("VFX") ? "VFX" : "Default";
            r.sortingOrder     = sortingOrder;
        }

        /// <summary>
        /// Build the standard fade-in / hold / fade-out alpha gradient every layer wants:
        /// nothing may appear or vanish at a hard edge, because the emitter box sits just
        /// outside the viewport and a drop that pops on at full alpha does it on screen.
        /// </summary>
        protected static void ApplyLifetimeFade(WeatherLayer layer, float fadeIn, float fadeOut)
        {
            var col = layer.System.colorOverLifetime;
            col.enabled = true;

            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(fadeIn)),
                    new GradientAlphaKey(1f, Mathf.Clamp01(1f - fadeOut)),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);
        }

        /// <summary>
        /// Force the next frame to rewrite every layer's emission rate. Needed after anything
        /// that changes a rate WITHOUT changing the density — a re-layout that widened a
        /// spawn slab, for instance, whose <see cref="WeatherLayer.SpawnWidthScale"/> the
        /// gated write would otherwise never pick up.
        /// </summary>
        protected void InvalidateRates() => _appliedDensity = -1f;

        /// <summary>
        /// Position, size and time a layer that FALLS: a thin slab above the top edge, widened
        /// upwind by however far the wind carries a particle during its descent, and a
        /// randomised lifetime sized to the viewport it has to cross.
        ///
        /// The randomised lifetime is what stops every particle reaching the bottom edge
        /// together. Paired with the layer's tail fade it reads as precipitation stopping at
        /// different distances — the cheap approximation of ground contact that avoids a
        /// per-tile collision query per drop.
        /// </summary>
        protected static void LayoutFallingLayer(WeatherLayer layer, float marginW, float marginH,
                                                 float fallSpeed, float lifeMin, float lifeMax)
        {
            if (layer == null) return;

            float fall   = Mathf.Max(0.05f, fallSpeed);
            float travel = marginH * 2f;
            float viewW  = Mathf.Max(0.01f, marginW * 2f);

            // Clamped, because a slow-falling layer in a strong gust asks for a slab hundreds
            // of units wide — snow at 1.1 u/s crossing a 15 u drop is airborne for thirteen
            // seconds, so the honest drift at storm wind is over a hundred units. Past about
            // 1.5 screens the compensation is paying for particles nobody will ever see, and
            // the upwind thinning it would fix is invisible anyway because the snow is by then
            // arriving almost horizontally.
            float driftX = Mathf.Abs(WeatherWind.VelocityX) * layer.WindFactor;
            float drift  = Mathf.Min(driftX * (travel / fall), viewW * 1.5f);

            float spawnW  = viewW + drift * 1.15f;
            float centreX = -Mathf.Sign(WeatherWind.VelocityX) * drift * 0.5f;

            var shape = layer.System.shape;
            shape.scale    = new Vector3(spawnW, 0.5f, 0.1f);
            shape.position = new Vector3(centreX, marginH, 0f);

            layer.SpawnWidthScale = spawnW / viewW;

            // Time to leave the frame, whichever edge it reaches first. Sizing on the vertical
            // fall alone keeps a wind-blown flake alive for the whole thirteen seconds it
            // would need to reach the bottom, long after it has left the side of the screen —
            // which is simulation nobody sees, paid for on every frame.
            float full = travel / fall;
            if (driftX > 0.05f) full = Mathf.Min(full, spawnW / driftX);

            var main = layer.System.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(full * lifeMin, full * lifeMax);
        }

        /// <summary>
        /// Size a layer that fills the visible area rather than falling through it — splashes
        /// on the ground, settled snow, drifting haze. In a top-down game the ground IS the
        /// whole frame, so these spawn over the box, not along a horizon.
        /// </summary>
        protected static void LayoutAreaLayer(WeatherLayer layer, float marginW, float marginH)
        {
            if (layer == null) return;
            var shape = layer.System.shape;
            shape.scale    = new Vector3(marginW * 2f, marginH * 2f, 0.1f);
            shape.position = Vector3.zero;
        }

        /// <summary>
        /// Push the live wind into a layer's horizontal velocity, scaled by its
        /// <see cref="WeatherLayer.WindFactor"/>. A band rather than a constant: one identical
        /// horizontal speed across a whole layer makes it read as a rigid sheet being
        /// translated rather than as loose particles in moving air.
        /// </summary>
        protected static void ApplyWindTo(WeatherLayer layer, float windVelocityX)
        {
            if (layer == null || layer.WindFactor <= 0f) return;
            float vx = windVelocityX * layer.WindFactor;
            var velocity = layer.System.velocityOverLifetime;
            velocity.x = new ParticleSystem.MinMaxCurve(vx * 0.82f, vx * 1.18f);
        }

        private static bool SortingLayerExists(string name)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == name) return true;
            return false;
        }
    }
}
