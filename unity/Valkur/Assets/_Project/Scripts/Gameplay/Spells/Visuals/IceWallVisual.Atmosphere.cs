using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Everything around the crystals: the cold aura behind them, the haze and frost motes
    /// above them, and the lights they cast.
    /// </summary>
    internal sealed partial class IceWallVisual
    {
        /// <summary>
        /// A few wide, faint glows sitting BEHIND the row. They do the job the halo of a
        /// radial rig would do — separate the effect from the ground — but spread along the
        /// line, so a long wall does not end up with a bright spot in the middle and nothing
        /// at its ends.
        /// </summary>
        private void BuildAura()
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / 1.6f), 2, 8);
            float size = Mathf.Max(1.2f, _config.Height * 1.7f);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                var go = new GameObject("Aura");
                go.transform.SetParent(_root, false);
                go.transform.localPosition =
                    AlongAxis((t - 0.5f) * _config.Length) +
                    new Vector3(0f, BackRowOffsetY + _config.Height * 0.28f, 0f);
                go.transform.localScale = new Vector3(size * 1.25f, size, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.Glow;
                sr.color = new Color(0.42f, 0.76f, 1f, 0f);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                // Behind the back row: its own Y minus a margin larger than any part offset.
                sr.sortingOrder = OrderAt(BackRowOffsetY, -12);
                _auras.Add(sr);
            }
        }

        private void BuildParticles()
        {
            _mist = BuildEmitter("Mist", ElementalSprites.Glow, additive: true, order: 55);
            ConfigureMist(_mist);

            _sparkle = BuildEmitter("FrostMotes", ElementalSprites.SparkleStar, additive: true, order: 56);
            ConfigureMotes(_sparkle);
        }

        /// <summary>
        /// Both emitters share the same box: a slab as long as the wall, as deep as its
        /// footprint and as tall as the crystals. A circle — which is what the old rig
        /// emitted from — puts every particle in the middle of the barrier.
        /// </summary>
        private ParticleSystem BuildEmitter(string name, Sprite sprite, bool additive, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, _config.Height * 0.35f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(_config.Axis.y, _config.Axis.x) * Mathf.Rad2Deg);

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately, and Unity refuses a duration write
            // on a playing system with a warning while silently keeping the old value.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_config.Length, _config.Height * 0.7f, 0.01f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterialCache.Get(sprite.texture, additive);
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = order;
            return ps;
        }

        /// <summary>Slow cold haze lifting off the crystals. Large, faint, and few.</summary>
        private void ConfigureMist(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 999f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            // Zero start speed on purpose: a Box shape emits along its own FORWARD, which in
            // a 2D scene is straight into the screen, so startSpeed buys motion nobody can
            // see. All the drift is authored on velocityOverLifetime below instead.
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.84f, 1f, 1f), new Color(0.80f, 0.95f, 1f, 1f));
            main.gravityModifier = 0f;
            main.maxParticles = 220;

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(_config.Length * 3.2f, 8f, 40f);

            SetPlanarVelocity(ps, horizontal: 0.12f, riseMin: 0.10f, riseMax: 0.35f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.35f)));

            ApplyFade(ps, peak: 0.30f, holdFrom: 0.15f, holdTo: 0.55f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.16f);
            noise.frequency = 0.28f;
            noise.damping = true;

            ps.Play();
        }

        /// <summary>Twinkling frost motes. Tiny, bright, and the thing that reads as "cold".</summary>
        private void ConfigureMotes(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 999f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);   // see ConfigureMist
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.26f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.88f, 0.98f, 1f, 1f), new Color(0.55f, 0.85f, 1f, 1f));
            main.gravityModifier = 0.04f;
            main.maxParticles = 260;

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(_config.Length * 4.5f, 10f, 55f);

            SetPlanarVelocity(ps, horizontal: 0.26f, riseMin: -0.08f, riseMax: 0.34f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f);

            // Twinkle: two peaks over the life, so a mote catches the light more than once.
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.18f),
                    new GradientAlphaKey(0.35f, 0.45f),
                    new GradientAlphaKey(0.85f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            ps.Play();
        }

        /// <summary>
        /// Drift in the XY plane, with all three axes written in the SAME curve mode.
        ///
        /// <para>Assigning only <c>velocity.y</c> as a two-constant range leaves x and z as
        /// single constants, and Unity rejects the mismatch with "Particle Velocity curves
        /// must all be in the same mode" — once per frame, per system, for as long as the
        /// effect lives. Writing all three is the fix, not a tidy-up.</para>
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

        private static void ApplyFade(ParticleSystem ps, float peak, float holdFrom, float holdTo)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, holdFrom),
                    new GradientAlphaKey(peak, holdTo),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;
        }

        /// <summary>
        /// Lights spread along the barrier rather than one at its centre. They hang on the
        /// UNSCALED root, which is the whole reason this rig refuses to scale it.
        /// </summary>
        private void BuildLights()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / 2.2f), 1, 5);
            _lightBaseIntensity = 1.35f;
            float radius = Mathf.Max(1.4f, _config.Height * 1.6f);

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                var go = new GameObject("IceLight");
                go.transform.SetParent(_root, false);
                go.transform.localPosition =
                    AlongAxis((t - 0.5f) * _config.Length * 0.86f) +
                    new Vector3(0f, _config.Height * 0.3f, 0f);

                try
                {
                    var light = go.AddComponent(lightType);
                    // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. A placed light is a Point.
                    var lightTypeProperty = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    lightTypeProperty?.SetValue(light, System.Enum.ToObject(lightTypeProperty.PropertyType, 3));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(light, new Color(0.55f, 0.85f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(light, 0f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(light, radius);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(light, radius * 0.25f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(light, 0.9f);
                    _lightComponents.Add(light);
                }
                catch
                {
                    // A URP version without the expected properties must not take the wall
                    // down with it: the crystals are the effect, the light is the polish.
                }
                _lights.Add(go);
            }
        }

        private void SetLightIntensity(float intensity)
        {
            var property = ElementalProjectileVisual.GetLight2DIntensityProp();
            if (property == null) return;
            for (int i = 0; i < _lightComponents.Count; i++)
            {
                if (_lightComponents[i] == null) continue;
                try { property.SetValue(_lightComponents[i], intensity); }
                catch { }
            }
        }
    }
}
