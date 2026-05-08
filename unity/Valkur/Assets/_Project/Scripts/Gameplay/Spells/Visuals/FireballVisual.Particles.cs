using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// FireballVisual — particle systems and trail partial.
    ///
    /// Owns:
    ///   • "Core_Particles"  — dense bright shimmer packed INSIDE the orb body
    ///                         (simulationSpace = Local, near-zero velocity, additive,
    ///                          gives the ball a "made of fire particles" texture)
    ///   • "Sparks_Orbit"    — pinpoint sparks orbiting at ~1.2× core radius
    ///                         (simulationSpace = Local, fast tangential rotation, additive)
    ///   • TrailRenderer "Trail" — warm fire trail (disabled when legacy ghost trail active)
    ///
    /// Pooling contract (Domain Reload OFF):
    ///   Built once in Awake; cleared/reset in OnEnable; stopped/cleared in OnDisable.
    ///   Never destroyed on OnDisable — they are persistent children.
    ///
    /// Additive material cache is static; reset via SubsystemRegistration.
    /// </summary>
    public partial class FireballVisual
    {
        // ── Additive material (shared static) ─────────────────────────
        private static Material _additiveMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticParticles()
        {
            _additiveMaterial = null;
        }

        private static Material GetAdditiveMaterial()
        {
            if (_additiveMaterial != null) return _additiveMaterial;

            // Prefer URP Particles/Unlit with additive blend, fall back to Sprites/Default
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            _additiveMaterial = new Material(shader);

            // Enable additive blending: SrcFactor=One DstFactor=One (_BlendMode int = 1 in URP particles)
            if (_additiveMaterial.HasProperty("_BlendMode"))
                _additiveMaterial.SetFloat("_BlendMode", 1f);

            // Fallback manual blend for Sprites/Default
            _additiveMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _additiveMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);

            return _additiveMaterial;
        }

        // ── Per-instance particle / trail handles ─────────────────────
        private ParticleSystem _corePs;
        private ParticleSystem _sparksPs;
        private TrailRenderer  _trail;

        // ── Build ─────────────────────────────────────────────────────

        private void BuildCoreParticles()
        {
            var go = new GameObject("Core_Particles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _corePs = go.AddComponent<ParticleSystem>();

            var main = _corePs.main;
            main.loop             = true;
            main.playOnAwake      = true;
            // Local space: shimmer follows the orb instead of trailing in world.
            main.simulationSpace  = ParticleSystemSimulationSpace.Local;
            main.startLifetime    = new ParticleSystem.MinMaxCurve(CoreParticleLifetimeMin, CoreParticleLifetimeMax);
            main.startSize        = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startSpeed       = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.maxParticles     = 80;
            // Start colour: white-yellow → orange (randomised between two presets)
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.6f, 1f),
                new Color(1f, 0.55f, 0.1f, 1f));

            var emission = _corePs.emission;
            emission.rateOverTime = CoreParticleEmitRate;

            // Emission shape: tiny core volume so particles pack tightly inside the orb.
            var shape = _corePs.shape;
            shape.enabled  = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius    = CoreScale * 0.15f;
            shape.radiusThickness = 1f; // fill the disc, not just shell

            // Colour over lifetime: white-yellow → orange → fade
            var col = _corePs.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0.0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.6f),
                    new GradientColorKey(new Color(0.8f, 0.2f, 0.0f), 1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0.0f),
                    new GradientAlphaKey(0.7f, 0.5f),
                    new GradientAlphaKey(0f, 1.0f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Tiny radial drift in Local space — particles "breathe" outward a hair
            // before dying, but never escape the orb body.
            var vel = _corePs.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.Local;
            vel.radial  = new ParticleSystem.MinMaxCurve(0f, 0.1f);

            // Renderer: additive, sorted between glow and hot-core (order + 4 ... + 5)
            var rend = _corePs.GetComponent<ParticleSystemRenderer>();
            rend.material         = GetAdditiveMaterial();
            rend.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            rend.sortingOrder     = SortingConfig.Z_SKY + 5; // between glow (+3) and hot-core (+6)
            rend.renderMode       = ParticleSystemRenderMode.Billboard;
        }

        private void BuildOrbitingSparks()
        {
            var go = new GameObject("Sparks_Orbit");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _sparksPs = go.AddComponent<ParticleSystem>();

            var main = _sparksPs.main;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // follows fireball
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 0f);
            main.maxParticles    = 30;
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 0.85f, 1f),
                new Color(1f, 0.7f, 0.2f, 1f));

            var emission = _sparksPs.emission;
            emission.rateOverTime = SparkOrbitEmitRate;

            // Emit from a thin shell well outside the core so sparks never overlap the orb.
            var shape = _sparksPs.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius    = CoreScale * SparkOrbitRadiusMul;
            shape.radiusThickness = 0f;           // emit from shell only

            // Colour over lifetime: bright white → warm orange → fade
            var col = _sparksPs.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 1f, 0.9f),   0.0f),
                    new GradientColorKey(new Color(1f, 0.65f, 0.1f), 0.5f),
                    new GradientColorKey(new Color(0.9f, 0.3f, 0.0f), 1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0.0f),
                    new GradientAlphaKey(0.8f, 0.4f),
                    new GradientAlphaKey(0f, 1.0f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Orbital velocity: pure tangential rotation around Z. No radial drift —
            // sparks must hold the orbit so they read as "rotating around the orb",
            // not "spraying outward".
            var vel = _sparksPs.velocityOverLifetime;
            vel.enabled  = true;
            vel.space    = ParticleSystemSimulationSpace.Local;
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(SparkOrbitalSpeedMin, SparkOrbitalSpeedMax);
            vel.radial   = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Renderer: additive, sorted in front of glow but behind core
            var rend = _sparksPs.GetComponent<ParticleSystemRenderer>();
            rend.material         = GetAdditiveMaterial();
            rend.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            rend.sortingOrder     = SortingConfig.Z_SKY + 4; // sparks behind core (+5)
            rend.renderMode       = ParticleSystemRenderMode.Billboard;
        }

        private void BuildTrail()
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _trail = go.AddComponent<TrailRenderer>();
            _trail.time           = TrailTime;
            _trail.startWidth     = GlowScale * TrailStartWidthMul;
            _trail.endWidth       = 0f;
            _trail.numCapVertices = 4;
            _trail.material       = GetAdditiveMaterial();
            _trail.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _trail.sortingOrder     = SortingConfig.Z_SKY + 2; // behind glow (+3)
            _trail.alignment        = LineAlignment.View;
            _trail.textureMode      = LineTextureMode.Stretch;
            _trail.generateLightingData = false;
            _trail.autodestruct     = false;

            // Warm-yellow → orange → red → transparent gradient
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.55f), 0.0f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.05f), 0.5f),
                    new GradientColorKey(new Color(0.7f, 0.1f, 0.0f), 1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.9f, 0.0f),
                    new GradientAlphaKey(0.5f, 0.5f),
                    new GradientAlphaKey(0f,   1.0f),
                });
            _trail.colorGradient = grad;

            // Active state driven by inspector toggles (set once here, read in OnEnable)
            _trail.enabled = _useNewTrail && !_useLegacyGhostTrail;
        }

        // ── Per-frame hook ────────────────────────────────────────────

        /// <summary>
        /// No-op placeholder for the per-frame hook called from the main partial.
        /// Core particles use simulationSpace = World, so they naturally stay at
        /// their emission positions regardless of projectile movement — no manual
        /// velocity injection needed. The method is kept so the call-site in
        /// FireballVisual.cs (Update) compiles and can be extended if desired.
        /// </summary>
        private void TickCoreParticleVelocity(Vector3 delta)
        {
            // World-space simulation already provides the "ember swarm" left behind.
            // TODO: consider ParticleSystem.Emit with custom velocity when a tighter
            // "particles move with the ball" look is needed (e.g. low-speed shots).
            _ = delta; // suppress unused-parameter warning
        }

        // ── Pooling lifecycle ─────────────────────────────────────────

        private void ResetParticlesOnEnable()
        {
            // Apply trail active state from inspector booleans each enable
            bool trailActive = _useNewTrail && !_useLegacyGhostTrail;
            if (_trail != null)
            {
                _trail.enabled = trailActive;
                if (trailActive) _trail.Clear();
            }

            // Sync ghost sprite visibility with inspector boolean
            if (_ghostSrs != null)
            {
                for (int i = 0; i < _ghostSrs.Length; i++)
                {
                    if (_ghostSrs[i] != null)
                        _ghostSrs[i].enabled = _useLegacyGhostTrail;
                }
            }

            // Clear and restart particle systems
            if (_corePs != null)
            {
                _corePs.Clear(true);
                _corePs.Play(true);
            }

            if (_sparksPs != null)
            {
                _sparksPs.Clear(true);
                _sparksPs.Play(true);
            }

        }

        private void StopParticlesOnDisable()
        {
            if (_corePs != null)
                _corePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (_sparksPs != null)
                _sparksPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (_trail != null)
                _trail.Clear();
        }
    }
}
