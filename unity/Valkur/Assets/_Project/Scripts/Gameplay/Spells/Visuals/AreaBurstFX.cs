using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The burst rig every <c>SpellType.Area</c> spell gets, shaped by
    /// <see cref="AreaBurstProfile"/>.
    ///
    /// <para>It exists because <c>AreaExecutor</c>'s entire visual output was one call to
    /// <c>VFXManager.SpawnAreaIndicator</c>: a single SpriteRenderer with a radial-falloff
    /// texture, NO material assigned — so it inherited Unity's default LIT sprite material and
    /// dimmed at night — no light, no particles, 0.5 s. All five shipped Area spells were that
    /// same soft disc in five hues, and two pairs of those hues were within 0.08 of each other.
    /// A frost nova, a thorn eruption and a thunderclap are not the same event.</para>
    ///
    /// <para>WHY NOT <c>AreaFXRig</c>. That rig is four concentric discs and a circle emitter,
    /// and a stack of coplanar discs can draw neither a wave FRONT nor anything that STANDS UP.
    /// It is the single most repeated mistake in this codebase — <c>IceWallVisual</c> records it
    /// for a line, <c>VortexFunnelFX</c> for a column, <c>FlameConeFX</c> for a wedge and
    /// <c>RootWhipFX</c> for a root — and it is why these five spells scored 1.5 to 2.8.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED. Every child carries an absolute world size, which is what
    /// keeps the <c>Light2D</c> rendering at its authored radius: a rig that both sizes its
    /// children and scales its root renders the light at <c>authored x lossyScale</c>, the
    /// failure that once lit 367 world units off a 21-unit light.</para>
    /// </summary>
    internal sealed partial class AreaBurstFX : MonoBehaviour
    {
        /// <summary><c>ElementalSprites.Ring</c>'s bright band peaks at this normalized radius,
        /// so a wanted world radius divided by it is the scale that puts the drawn circle
        /// exactly there. Law L5, and the same constant <c>RootWhipFX</c> uses.</summary>
        private const float RING_BAND = 0.39f;

        /// <summary>How flat a horizontal circle is drawn. The camera looks down at a shallow
        /// angle, so anything lying on the ground plane is a wide thin ellipse — the same
        /// constant the vortex funnel, the root field and the ki aura pulses use.</summary>
        private const float GROUND_SQUASH = 0.34f;

        /// <summary>Ceiling on the event layer, so the sorting orders below are DERIVED from a
        /// constant rather than from a per-instance count. A literal there is the bug that sank
        /// the vortex debris behind its own funnel when the band count changed.</summary>
        private const int MAX_EVENTS = 24;

        private const int ORDER_HAZE = 2;
        private const int ORDER_GROUND_RING = 4;
        private const int ORDER_WAVE = 6;
        private const int ORDER_EVENT = 10;
        private const int ORDER_GRIT = ORDER_EVENT + MAX_EVENTS + 1;
        private const int ORDER_CORE = ORDER_GRIT + 1;

        private AreaBurstProfile _profile;

        private SpriteRenderer _wave;
        private SpriteRenderer _groundRing;
        private SpriteRenderer _haze;
        private SpriteRenderer _core;
        private SpriteRenderer[] _cracks;
        private Color _crackColor;
        private Component _light;

        private float[] _eventRadius;
        private float[] _eventAngle;
        private bool[] _eventFired;

        private float _age;
        private float _seed;

        /// <summary>How far the wave front has travelled this frame, world units.</summary>
        internal float WaveRadius => _profile.Radius * WaveFraction01(_age);

        /// <summary>
        /// Fire one burst at <paramref name="center"/>. The Snare silhouette is a PERSISTENT
        /// field rather than a burst and is handed to its own rig here, so the executor never
        /// has to know which of the six it is casting.
        /// </summary>
        internal static void Play(Vector3 center, AreaBurstProfile profile, Transform caster,
                                  List<GameObject> caught)
        {
            ElementalSprites.EnsureAll();

            if (profile.Silhouette == AreaSilhouette.Snare)
            {
                AreaEntangleFX.Play(center, profile, caught);
                return;
            }

            var go = new GameObject("AreaBurst_" + profile.Silhouette);
            go.transform.position = center;
            go.AddComponent<AreaBurstFX>().Begin(profile, caster);
        }

        private void Begin(AreaBurstProfile profile, Transform caster)
        {
            _profile = profile;
            _seed = Random.Range(0f, 100f);

            BuildGroundRing();
            BuildWave();
            BuildHaze();
            BuildCore();
            BuildCracks();
            SeedEvents();
            BuildLight();
            ThrowGrit();

            // A clap and a detonation are events in the ROOM. A local Light2D alone reads as a
            // flare; SkyFlash composes into DayNightCycle.UpdateLighting and lifts the whole
            // scene, which is what separates the two.
            if (_profile.SkyFlash > 0f)
                SkyFlash.Pulse(_profile.Palette.lightColor, _profile.SkyFlash, _profile.Life * 0.9f);

            // WHY THIS IS A BLOOM AND NOT A TintLayer. Law L9 is that the entity body's colour
            // has exactly one owner, and every one of SpriteTintStack's sixteen layers already
            // HAS one — Cast belongs to SpellCastFlourishFX, which is writing it during this
            // very cast. Adding a second writer is the defect the stack exists to prevent.
            // And it could not have worked anyway: the stack MULTIPLIES, so a white tint on a
            // white-based sprite is a no-op — the only thing multiply can do is darken the
            // caster, which is the opposite of what a light detonating on them should look
            // like. An additive bloom OVER the silhouette is what actually washes a body out,
            // and it is the same mechanism WeaponSwapFlashFX uses for the same reason.
            if (_profile.CasterTint.a > 0f && caster != null)
                AreaBurstPieces.CasterBloom(caster, _profile);
        }

        /// <summary>
        /// The CONTRACT layer: pinned to the damage circle and never moved. Everything else in
        /// the rig is scattered or travelling and therefore promises nothing exact.
        /// </summary>
        private void BuildGroundRing()
        {
            if (!_profile.HasGroundRing) return;
            _groundRing = MakeSprite("GroundRing", ElementalSprites.Ring,
                                     _profile.Palette.core, ORDER_GROUND_RING, additive: true);
            SetGroundSize(_groundRing.transform, _profile.Radius / RING_BAND);
        }

        private void BuildWave()
        {
            _wave = MakeSprite("Wave", ElementalSprites.Ring,
                               _profile.Palette.hotCore, ORDER_WAVE, additive: true);
            SetGroundSize(_wave.transform, 0f);
        }

        private void BuildHaze()
        {
            if (!_profile.HasHaze) return;
            _haze = MakeSprite("Haze", ElementalSprites.Glow,
                               _profile.Palette.glow, ORDER_HAZE, additive: true);
            SetGroundSize(_haze.transform, 0f);
        }

        private void BuildCore()
        {
            // Only the two silhouettes whose subject is the FLASH carry one. On the others a
            // bright blob at the centre competes with the wave for the same attention and the
            // wave is the thing that means something.
            if (_profile.Silhouette != AreaSilhouette.Radiance &&
                _profile.Silhouette != AreaSilhouette.Shock) return;

            _core = MakeSprite("Core", ElementalSprites.HotCore,
                               _profile.Palette.hotCore, ORDER_CORE, additive: true);
            _core.transform.localScale = Vector3.one * (_profile.Radius * 0.55f);
        }

        /// <summary>
        /// The "before". Thorns is the one silhouette with a cause that precedes its effect —
        /// spikes from below are only legible if the ground is seen to open first — so the
        /// cracks are built at t=0 and the thorns cannot fire until the wave reaches them.
        /// </summary>
        private void BuildCracks()
        {
            if (_profile.Silhouette != AreaSilhouette.Thorns) return;

            // Resolved once. RootPalette.From does an HSV round trip, and the crack colour
            // cannot change over the burst's life — only its alpha does.
            _crackColor = RootPalette.From(_profile.Swatch).Soil;

            _cracks = new SpriteRenderer[9];
            for (int i = 0; i < _cracks.Length; i++)
            {
                var sr = MakeSprite("Crack" + i, RootSprites.Crack, _crackColor,
                                    ORDER_GROUND_RING + 1, additive: false);
                // Jittered off an even fan: a perfect star reads as a decal stamped on the
                // floor, which is the one thing the cracks exist to deny.
                float bearing = (i / (float)_cracks.Length) * 360f + Random.Range(-14f, 14f);
                float reach = _profile.Radius * Random.Range(0.35f, 0.95f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, bearing);
                // The fissure runs along +X from its pivot, so the rotation aims it and
                // localScale.x is the reach. Y takes the ground squash because a fissure lying
                // on the floor is foreshortened exactly as the ring is — the same proportion
                // RootWhipFX measured for the same sprite.
                sr.transform.localScale = new Vector3(reach, reach * GROUND_SQUASH * 1.4f, 1f);
                _cracks[i] = sr;
            }
        }

        /// <summary>
        /// Each event gets an angle and a RADIUS, and fires when the wave front passes it —
        /// which is what turns an expanding circle into a wave front that is doing something.
        /// It costs one float comparison per event per frame.
        /// </summary>
        private void SeedEvents()
        {
            int count = Mathf.Clamp(_profile.EventCount, 0, MAX_EVENTS);
            _eventRadius = new float[count];
            _eventAngle = new float[count];
            _eventFired = new bool[count];

            for (int i = 0; i < count; i++)
            {
                // Evenly spaced bearings with a jitter, rather than uniform random: a random
                // ring of fourteen leaves visible gaps and clumps, and the gap reads as the
                // spell having failed there.
                float slice = Mathf.PI * 2f / Mathf.Max(1, count);
                _eventAngle[i] = i * slice + Random.Range(-slice * 0.35f, slice * 0.35f);
                _eventRadius[i] = _profile.Radius *
                    Random.Range(_profile.EventBandMin, _profile.EventBandMax);
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("AreaBurstLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. Passing the wrong literal
                // here is what once left every placed torch a cookie-less Sprite light.
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _profile.Palette.lightColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _profile.LightRadius);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _profile.LightRadius * 0.12f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch
            {
                _light = null;
                Destroy(go);
            }
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            // Law L6: LAYER_VFX with a SMALL order. SortingConfig.Z_SKY is a Z DEPTH, and
            // passing it here drew every bolt under the wall tops — recorded twice already.
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>A circle lying on the floor is a wide thin ellipse, so one call sets both
        /// axes from a single world diameter and nothing can set them independently.</summary>
        private static void SetGroundSize(Transform t, float diameter)
            => t.localScale = new Vector3(diameter, diameter * GROUND_SQUASH, 1f);
    }
}
