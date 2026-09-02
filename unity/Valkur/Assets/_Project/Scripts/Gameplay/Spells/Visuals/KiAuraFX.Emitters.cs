using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two particle systems: the ki streaming upward off the body, and the ground coming
    /// apart underneath it.
    /// </summary>
    internal sealed partial class KiAuraFX
    {
        private void BuildEmitters()
        {
            _kiStream = BuildSystem("KiStream", KiSprites.Streak, additive: true,
                sortingLayer: SortingConfig.LAYER_ENTITIES, order: KI_STREAM_ORDER);
            _kiStreamRenderer = _kiStream.GetComponent<ParticleSystemRenderer>();
            ConfigureKiStream(_kiStream);

            _debris = BuildSystem("Debris", KiSprites.Pebble, additive: false,
                sortingLayer: SortingConfig.LAYER_ENTITIES, order: DEBRIS_ORDER);
            _debrisRenderer = _debris.GetComponent<ParticleSystemRenderer>();
            ConfigureDebris(_debris);
        }

        private ParticleSystem BuildSystem(string name, Sprite sprite, bool additive,
            string sortingLayer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately, and Unity refuses a duration write
            // on a playing system with a warning while silently keeping the old value.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterialCache.Get(sprite.texture, additive);
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = order;
            return ps;
        }

        /// <summary>
        /// Sparks tearing off the body and climbing. The single most recognisable part of a ki
        /// charge, so it gets the widest intensity range of anything here: a calm aura sheds a
        /// few, a violent one is a chimney.
        /// </summary>
        private void ConfigureKiStream(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 999f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            // Zero start speed: a Box shape emits along its own FORWARD, which in a 2D scene
            // is straight into the screen. All the motion is authored below.
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.gravityModifier = 0f;
            main.maxParticles = 420;

            // A spark is TALL. startSize is one number applied to a square quad, so without
            // the 3D form the streak texture is squashed back into a blob.
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.34f, 0.62f, Intensity), Mathf.Lerp(0.70f, 1.30f, Intensity));
            main.startColor = new ParticleSystem.MinMaxGradient(
                _config.Palette.Core, _config.Palette.Mid);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_config.BodySize.x * 1.25f, _config.BodySize.y * 0.55f, 0.01f);
            shape.position = new Vector3(0f, _config.BodySize.y * 0.30f, 0f);

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Lerp(18f, 130f, Intensity);

            SetPlanarVelocity(ps, horizontal: 0.35f,
                riseMin: Mathf.Lerp(2.2f, 4.5f, Intensity),
                riseMax: Mathf.Lerp(4.5f, 9.5f, Intensity));

            // Accelerating upward, which is what makes the stream read as being DRIVEN rather
            // than as smoke drifting off something.
            var limit = ps.forceOverLifetime;
            limit.enabled = true;
            limit.space = ParticleSystemSimulationSpace.World;
            limit.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            limit.y = new ParticleSystem.MinMaxCurve(Mathf.Lerp(1.5f, 6f, Intensity));
            limit.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.65f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            ps.Play();
        }

        /// <summary>
        /// Chips of ground lifting and turning. Emits NOTHING below a third intensity, and
        /// that silence is the point: a calm charge glows, it does not break the floor. This
        /// is also the only opaque layer in the effect, which is what makes it read as debris
        /// rather than as more light.
        /// </summary>
        private void ConfigureDebris(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 999f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.19f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;   // the pebble carries its own grey
            // Negative gravity, so a chip keeps ACCELERATING away from the floor instead of
            // drifting at a constant speed — the tell that it is being pulled, not thrown.
            main.gravityModifier = Mathf.Lerp(-0.15f, -0.85f, Intensity);
            main.maxParticles = 120;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Flattened on Y: the ground is an ellipse from this camera, not a circle.
            shape.scale = new Vector3(_config.GroundRadius * 2f, _config.GroundRadius * 0.75f, 0.01f);

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, (Intensity - 0.32f) * 34f);

            SetPlanarVelocity(ps, horizontal: 0.22f, riseMin: 0.35f, riseMax: 1.30f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-2.6f, 2.6f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(1f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            ps.Play();
        }

        /// <summary>
        /// Drift in the XY plane, with all three axes written in the SAME curve mode.
        /// Assigning only <c>y</c> as a range leaves x and z as single constants, and Unity
        /// rejects the mismatch with "Particle Velocity curves must all be in the same mode" —
        /// once per frame, per system, for as long as the effect lives.
        /// </summary>
        private static void SetPlanarVelocity(ParticleSystem ps, float horizontal,
            float riseMin, float riseMax)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-horizontal, horizontal);
            velocity.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        /// <summary>
        /// Stop emitting while letting everything already in the air finish. Destroying the
        /// systems outright kills them on a frame boundary, which is how a charge that is
        /// meant to die down instead vanishes.
        /// </summary>
        private void StopEmitting()
        {
            if (_kiStream != null) _kiStream.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_debris != null) _debris.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
