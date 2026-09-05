using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A PILLAR WITH A FLOOR: an opaque stone shaft standing in the world, a ring pinned to the
    /// circle it actually heals, a low dome of light over that circle, and motes lifting off
    /// the ground inside it.
    ///
    /// <para>THE SHAFT IS THE POINT. It is the rig's one OPAQUE layer and the only thing in the
    /// whole set that says something was PLACED in the world rather than shone onto it — which
    /// is why it is drawn on <c>Entities</c> with a Y-sorted order, so the player can walk
    /// behind it. Everything else here is additive light and would happily have been a decal.
    /// The predecessor was <see cref="AreaFXRig"/>'s four concentric discs with a triangle
    /// sprite behind them, which draws a totem the way a puddle draws a puddle.</para>
    ///
    /// <para>THE EVENT LAYER IS THE HEAL TICK, and it is synchronised to the ACTUAL heal —
    /// <see cref="Pulse"/> is called from the sweep, not from a decorative timer. That makes the
    /// beat carry literal mechanical information: the player can count the ticks. It is a rare
    /// case where a VFX layer is a readout, and the reason it is worth more than a prettier
    /// idle animation.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED. Every child carries an absolute world size — the only
    /// thing that keeps the <c>Light2D</c> rendering at the radius it was given.
    /// <c>TotemController</c> used to write <c>localScale = one * radius</c> straight after
    /// <c>AreaFXRig.Attach</c> had already sized every child by it.</para>
    /// </summary>
    internal sealed partial class SanctuaryPillarFX
    {
        /// <summary>World height of the stone shaft. Chest-high on a 1.86-unit character: tall
        /// enough to be an object, short enough not to hide the fight going on around it.</summary>
        private const float SHAFT_HEIGHT = 1.40f;
        private const float SHAFT_WIDTH = 0.46f;

        private const float GROUND_SQUASH = 0.34f;
        private const float RING_BAND = 0.39f;

        /// <summary>How many ripples can be in the air at once. One per healed body, and the
        /// circle rarely holds more than this many friendly targets.</summary>
        private const int RIPPLES = 4;
        private const float RIPPLE_SECONDS = 0.55f;

        /// <summary>
        /// The sharp part of a heal beat. Held against a 1.0 s <c>tickPeriod</c> this is ~22 %
        /// duty, which is the band an event layer sits in: below about 15 % nothing is
        /// happening, above 40 % it is a steady state again and the eye stops reporting it.
        /// </summary>
        private const float PULSE_SECONDS = 0.22f;

        /// <summary>Seconds the ground wave takes to travel from the shaft to the rim.</summary>
        private const float WAVE_SECONDS = 0.62f;

        /// <summary>Motes released together on a heal tick. The WAVE is the event; a steady
        /// drizzle at the same total rate would say nothing about when the heal landed.</summary>
        private const int PULSE_MOTES = 16;

        private const int ORDER_DOME = 39;
        private const int ORDER_RING = 42;
        private const int ORDER_WAVE = 43;
        private const int ORDER_RIPPLE = 6;

        private Transform _root;
        private float _radius;
        private ElementPalette _palette;

        private SpriteRenderer _shaft;
        private SpriteRenderer _band;
        private SpriteRenderer _capital;
        private SpriteRenderer _ring;
        private SpriteRenderer _dome;
        private SpriteRenderer _wave;
        private ParticleSystem _motes;

        private Transform[] _ripples;
        private SpriteRenderer[] _rippleRenderers;
        private float[] _rippleAge;
        private int _rippleCursor;

        private GameObject _lightGo;
        private Component _light;

        private float _age;
        private float _fade = 1f;
        private float _pulse;
        private float _waveAge = float.MaxValue;   // starts spent, so nothing draws until a tick
        private bool _destroyed;

        /// <summary>The circle the ground ring is drawn on, which is the HEAL radius.</summary>
        public float GroundRadius => _radius;

        /// <summary>The scale that puts <see cref="ElementalSprites.Ring"/>'s bright band on a
        /// given world radius, so a test can assert the composition and not either half.</summary>
        public static float RingSpanFor(float worldRadius) => worldRadius / RING_BAND;

        public static SanctuaryPillarFX Attach(Transform parent, float radius, ElementPalette palette)
        {
            ElementalSprites.EnsureAll();
            FieldSprites.EnsureAll();

            var fx = new SanctuaryPillarFX
            {
                _root = parent,
                _radius = Mathf.Max(0.5f, radius),
                _palette = palette,
            };

            parent.localScale = Vector3.one;

            fx.BuildGround();
            fx.BuildShaft();
            fx.BuildRipples();
            fx.BuildMotes();
            fx.AttachLight();
            return fx;
        }

        private void BuildGround()
        {
            // Pinned to the heal radius. Before this the totem healed exactly one entity — its
            // own caster — so the circle it drew promised an area nothing consulted; now the
            // circle and the sweep are the same number.
            float ringSpan = RingSpanFor(_radius);
            _ring = MakeSprite("SanctuaryRing", ElementalSprites.Ring, _palette.core, 0f,
                ORDER_RING, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _ring.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);

            // A low dome of light over the whole circle, at very low alpha. It is what makes
            // the inside of the ring feel like a different place from the outside.
            float domeSpan = _radius * 2.1f;
            _dome = MakeSprite("Dome", ElementalSprites.Halo, _palette.halo, 0f,
                ORDER_DOME, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _dome.transform.localScale = new Vector3(domeSpan, domeSpan * (GROUND_SQUASH + 0.16f), 1f);

            // The wave a heal tick sends out to the rim. Same sprite as the ring, so the two
            // agree about where the boundary is when the wave arrives at it.
            _wave = MakeSprite("HealWave", ElementalSprites.Ring, _palette.hotCore, 0f,
                ORDER_WAVE, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
        }

        private void BuildShaft()
        {
            // On Entities with a Y-sorted order, so a player standing below it draws in front
            // and one standing above it draws behind. That is the difference between an object
            // in the world and a sprite pasted over it.
            int order = SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, _root.position.y);

            _shaft = MakeSprite("Shaft", FieldSprites.Shaft, StoneColor(), 0f,
                order, SortingConfig.LAYER_ENTITIES, additive: false);
            FieldSprites.ScaleShaft(_shaft.transform, SHAFT_WIDTH, SHAFT_HEIGHT);

            // The gold band. It sits high on the shaft and it is the one place the spell's own
            // colour touches the stone, so the pillar reads as consecrated rather than as scenery.
            _band = MakeSprite("Band", FieldSprites.Streak, _palette.core, 0f,
                order + 1, SortingConfig.LAYER_ENTITIES, additive: true);
            _band.transform.localPosition = new Vector3(0f, SHAFT_HEIGHT * 0.78f, 0f);
            _band.transform.localScale = new Vector3(SHAFT_WIDTH * 1.35f, SHAFT_WIDTH * 1.35f, 1f);

            _capital = MakeSprite("Capital", ElementalSprites.Glow, _palette.hotCore, 0f,
                order + 2, SortingConfig.LAYER_ENTITIES, additive: true);
            _capital.transform.localPosition = new Vector3(0f, SHAFT_HEIGHT * 1.02f, 0f);
            _capital.transform.localScale = Vector3.one * (SHAFT_WIDTH * 2.4f);
        }

        /// <summary>Pale stone: the swatch's hue at low saturation and high value, so a totem
        /// authored gold is grey stone with a gold band rather than a gold pillar.</summary>
        private Color StoneColor()
        {
            Color.RGBToHSV(_palette.core, out float h, out float s, out _);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.22f), 0.86f);
        }

        private void BuildRipples()
        {
            _ripples = new Transform[RIPPLES];
            _rippleRenderers = new SpriteRenderer[RIPPLES];
            _rippleAge = new float[RIPPLES];

            for (int i = 0; i < RIPPLES; i++)
            {
                var sr = MakeSprite("Ripple" + i, ElementalSprites.Ring, _palette.hotCore, 0f,
                    ORDER_RIPPLE, SortingConfig.LAYER_VFX, additive: true);
                _ripples[i] = sr.transform;
                _rippleRenderers[i] = sr;
                _rippleAge[i] = RIPPLE_SECONDS;    // spent
            }
        }

        private void BuildMotes()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(_root, false);

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent plays immediately and main.duration cannot be written while playing.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = 1.5f;
            main.startSpeed = 0f;
            main.startSize = 0.16f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startColor = WithAlpha(_palette.hotCore, 0.85f);

            // A slow idle drizzle so the circle is never dead, with the real statement saved
            // for the tick burst.
            var emission = ps.emission;
            emission.rateOverTime = 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _radius * 0.95f;
            shape.radiusThickness = 1f;
            // Squashed onto the ground plane, so motes lift off the FLOOR of the circle rather
            // than out of a vertical disc standing in the middle of it.
            shape.scale = new Vector3(1f, GROUND_SQUASH, 1f);

            // All three axes as TwoConstants, so they share a MinMaxCurveMode; mixing modes
            // logs "Particle Velocity curves must all be in the same mode" every frame.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Motes dissolve at head height rather than fading over their whole flight: a fade
            // that starts at birth reads as a fog, and this one is supposed to read as rising.
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(0.9f, 0.62f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.25f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ParticleMaterialCache.Get(
                ElementalSprites.Sparkle.texture, additive: true);
            renderer.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 1;

            ps.Play(true);
            _motes = ps;
        }

        private void AttachLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("SanctuaryLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localPosition = new Vector3(0f, SHAFT_HEIGHT * 0.6f, 0f);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));   // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.06f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.22f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                SetLightIntensity(0f);
            }
            catch { _light = null; }
        }

        private void SetLightIntensity(float intensity)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity); }
            catch { }
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;

            if (_motes != null) _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (_lightGo != null)
            {
                // Destroy is an outright ERROR in Edit Mode, where a test builds this directly.
                if (Application.isPlaying) Object.Destroy(_lightGo);
                else Object.DestroyImmediate(_lightGo);
            }
            _lightGo = null;
            _light = null;
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, float alpha,
            int order, string layer, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, alpha);
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
