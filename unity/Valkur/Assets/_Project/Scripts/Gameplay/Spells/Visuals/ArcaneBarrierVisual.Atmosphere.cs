using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Everything on and around the surface: the glyphs that drift across it, the motes that
    /// stream off it, and the light it throws into the world.
    /// </summary>
    internal sealed partial class ArcaneBarrierVisual
    {
        private sealed class Rune
        {
            public Transform Root;
            public SpriteRenderer Sr;
            public float Along, Up;
            public float Drift;      // world units per second along the barrier
            public float Age, Period;
        }

        private readonly List<Rune> _runes = new List<Rune>();
        private ParticleSystem _motes;
        private readonly List<GameObject> _lights = new List<GameObject>();
        private readonly List<Component> _lightComponents = new List<Component>();

        /// <summary>
        /// Glyphs floating on the plane, each fading in, drifting along the barrier, and going
        /// out somewhere else.
        ///
        /// <para>THIS IS THE EVENT LAYER AND IT IS NOT DECORATION. Everything else here moves
        /// CONTINUOUSLY — the weave shimmers, the motes rise, the sigils turn — and continuous
        /// motion at a steady rate stops being read after about a second: the eye files the
        /// whole surface as one texture. What resets that is something that APPEARS and is
        /// gone, which is the same argument that put crawling discharges on the vortex funnel.
        /// A glyph igniting is that, and it also happens to be the single clearest statement
        /// that the barrier is a made thing rather than a coloured pane.</para>
        /// </summary>
        private void BuildRunes()
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / 1.5f), 2, 8);

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Glyph");
                go.transform.SetParent(_root, false);
                go.transform.localScale = Vector3.one * Range(0.30f, 0.46f);

                var rune = new Rune
                {
                    Root = go.transform,
                    Sr = Paint(go, ArcaneSprites.Rune(_rng.Next(ArcaneSprites.RuneVariants)),
                        _palette.Rune, additive: true, SortingConfig.LAYER_ENTITIES,
                        OrderFor(Part.Rune)),
                    Period = Range(2.4f, 4.6f),
                    // Staggered start ages, or every glyph on the barrier ignites in unison and
                    // the layer becomes one big flash instead of several separate events.
                    Age = Range(0f, 4.6f),
                };
                PlaceRune(rune);
                _runes.Add(rune);
            }
        }

        /// <summary>Move a glyph somewhere new and give it a fresh drift. Called on respawn.</summary>
        private void PlaceRune(Rune rune)
        {
            rune.Along = Range(-0.42f, 0.42f) * _config.Length;
            rune.Up = Range(0.22f, 0.82f) * _config.Height;
            rune.Drift = Range(-0.22f, 0.22f);
            rune.Sr.sprite = ArcaneSprites.Rune(_rng.Next(ArcaneSprites.RuneVariants));
        }

        /// <summary>
        /// Motes streaming off the surface. The emitter is a SLAB as long as the barrier and
        /// as tall as it: a sphere or a circle — which is what a radial rig would give — puts
        /// every particle in the middle of the wall and none at its ends.
        /// </summary>
        private void BuildMotes()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, _config.Height * 0.5f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(_config.Axis.y, _config.Axis.x) * Mathf.Rad2Deg);

            _motes = go.AddComponent<ParticleSystem>();
            // AddComponent starts a ParticleSystem immediately (playOnAwake defaults true), and
            // main.duration cannot be written while it plays: the write is refused with a
            // warning and the old value silently kept. Stop, configure, Play.
            _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _motes.main;
            main.duration = 3f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = 0f;                       // a Box emits along its own FORWARD,
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);   // i.e. into the
            main.startColor = _palette.Rune;                                 // screen.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = _motes.emission;
            emission.rateOverTime = Mathf.Clamp(_config.Length * 5.5f, 8f, 46f);

            var shape = _motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_config.Length, _config.Height, 0.12f);

            var velocity = _motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // All three axes as two-constant ranges: assigning only one leaves the others as
            // single constants and Unity rejects the mismatch once per frame, per system.
            velocity.x = new ParticleSystem.MinMaxCurve(-0.16f, 0.16f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.30f, 0.85f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLifetime = _motes.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.22f),
                    new GradientAlphaKey(0.70f, 0.60f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            var renderer = _motes.GetComponent<ParticleSystemRenderer>();
            // A star, not a glyph: a mote is eight pixels across, and a mark that is
            // supposed to be READ at that size is just noise. The glyphs get their own layer
            // at a size where they can be made out.
            renderer.sharedMaterial = ParticleMaterialCache.Get(
                ElementalSprites.SparkleStar.texture, additive: true);
            renderer.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            renderer.sortingOrder = OrderFor(Part.Rune);

            _motes.Play();
        }

        /// <summary>
        /// One light per anchor post rather than one at the centre, so a long barrier lights
        /// the ground along its whole length. They hang on the UNSCALED root, which is the
        /// whole reason this rig refuses to scale it.
        /// </summary>
        private void BuildLights()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightBaseIntensity = 1.25f;
            float radius = Mathf.Max(1.3f, _config.Height * 1.15f);

            for (int i = 0; i < _posts.Count; i++)
            {
                var go = new GameObject("BarrierLight");
                go.transform.SetParent(_root, false);
                go.transform.localPosition =
                    AlongAxis(_posts[i].Along) + new Vector3(0f, _config.Height * 0.45f, 0f);

                try
                {
                    var light = go.AddComponent(lightType);
                    // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. A placed light is a Point.
                    var lightTypeProperty = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    lightTypeProperty?.SetValue(light, System.Enum.ToObject(lightTypeProperty.PropertyType, 3));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(light, _palette.Light);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(light, 0f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(light, radius);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(light, radius * 0.22f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(light, 0.9f);
                    _lightComponents.Add(light);
                }
                catch
                {
                    // A URP version without the expected properties must not take the barrier
                    // down with it: the weave is the effect, the light is the polish.
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
