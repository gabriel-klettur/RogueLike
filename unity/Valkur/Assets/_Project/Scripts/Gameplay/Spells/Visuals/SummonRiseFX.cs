using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The arrival IS the spell. After this the summon is an entity with its own animation,
    /// and the VFX budget belongs to its lifetime rather than its entrance
    /// (<see cref="SummonController"/> owns that half).
    ///
    /// <para>FIVE BEATS OVER ~0.95 s. A sigil opens on the ground, roots part, the creature
    /// comes UP THROUGH the floor over 0.4 s behind a heap of soil that then bursts outward,
    /// and only when it is standing does its brain get switched on. The rise is what separates
    /// a summon from a spawn: something that simply appears reads as a bug in the entity
    /// system, and the player has no moment to look at.</para>
    ///
    /// <para>THE EARTH IS THE ONE OPAQUE FAMILY (L3). The mound clods and the tendrils are
    /// both <c>ElementalSprites.SharedUnlitMaterial</c> and both say the same thing — the
    /// ground is being opened — while the ring, the burst and the motes are additive light.
    /// The mound is not decoration either: it is what OCCLUDES the lower half of the creature
    /// while it is still below the floor line, and without it the rise is a sprite sliding up
    /// out of nothing.</para>
    ///
    /// <para>The creature is spawned at the START of the rise, not at the end, precisely so
    /// there is something for the mound to hide. Its brain and colliders are switched off for
    /// the duration — the same thing <c>AllyDismissFX</c> does at the other end of the
    /// summon's life, and for the same reason: a creature that chases while it is still half
    /// underground reads as broken.</para>
    /// </summary>
    internal sealed partial class SummonRiseFX : MonoBehaviour
    {
        // ── The beats, in seconds from the cast ──────────────────────────────
        private const float T_SPAWN    = 0.20f;   // sigil is open; the body appears, sunk
        private const float T_THROW    = 0.38f;   // the mound bursts
        private const float T_STANDING = 0.60f;   // 0.40 s of rise is done; control handed over
        private const float T_END      = 0.95f;   // the light and the ring finish over it

        /// <summary>World radius the ground sigil is pinned to.</summary>
        private const float SIGIL_RADIUS = 1.15f;

        /// <summary>How far below the floor line the body starts.</summary>
        private const float RISE_DEPTH = 0.95f;

        private const int CLOD_COUNT = 10;
        private const int TENDRIL_COUNT = 5;
        private const int MOTE_COUNT = 14;

        private const int ORDER_RING    = 40;
        private const int ORDER_BURST   = 42;
        private const int ORDER_TENDRIL = 45;
        private const int ORDER_MOTE    = 47;
        private const int ORDER_CLOD    = 50;   // over the body: this is what hides the rise

        private MonsterDefinition _definition;
        private SpellDefinition _spell;
        private GameObject _caster;
        private float _lifetime;
        private float _healthScale = 1f;
        private bool _enforceCap;

        private RootPalette _palette;
        private Vector3 _position;
        private float _age;
        private bool _spawned;
        private bool _released;

        private GameObject _creature;
        private SpriteTintStack _bodyTint;
        private Transform _bodyRoot;
        private Vector3 _bodyRestPosition;

        private Transform _groundPlane;
        private SpriteRenderer _ring;
        private SpriteRenderer _burst;
        private SpriteRenderer[] _tendrils;
        private SpriteRenderer[] _clods;
        private Vector3[] _clodRest;
        private Vector2[] _clodVelocity;
        private SpriteRenderer[] _motes;
        private Vector3[] _moteDrift;
        private Component _light;

        /// <summary>
        /// Run the arrival at <paramref name="position"/>. Refused outside Play Mode for the
        /// reason every timed rig in this project is: the sequence advances from Update, which
        /// never runs there, so an Edit-Mode call would leave a permanent cluster of sprites.
        /// </summary>
        public static void Play(MonsterDefinition definition, Vector2 position, float lifetime,
                               SpellDefinition spell, GameObject caster, bool enforceCap,
                               float healthScale = 1f)
        {
            if (definition == null || !Application.isPlaying) return;

            var go = new GameObject("SummonRiseFX");
            go.transform.position = position;

            var fx = go.AddComponent<SummonRiseFX>();
            fx._definition = definition;
            fx._spell = spell;
            fx._caster = caster;
            fx._lifetime = lifetime;
            fx._enforceCap = enforceCap;
            fx._position = position;
            fx._healthScale = healthScale;
            // RootPalette rather than ElementPalette: this is soil and living wood, not an
            // element, and it derives all four ramps from the one authored swatch so a
            // designer cannot produce a plant whose earth is brighter than its sap.
            fx._palette = RootPalette.From(spell != null ? spell.particleColor : Color.white);
            fx.BuildRig();
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildRig()
        {
            ElementalSprites.EnsureAll();
            RootSprites.EnsureAll();

            // One squash parent for everything lying on the floor, with the rotation on the
            // children — squashing each item separately foreshortens its length without
            // turning its direction and it slides across the ground instead of lying on it.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            // L5: ElementalSprites.Ring peaks at normalized radius 0.78, so a boundary meant
            // to sit at world radius r is scaled r / 0.39.
            _ring = Additive("Sigil", ElementalSprites.Ring, _palette.Leaf, ORDER_RING, _groundPlane);
            _burst = Additive("Burst", RootSprites.Burst, _palette.Sap, ORDER_BURST, transform);

            BuildTendrils();
            BuildMound();
            BuildMotes();
            BuildLight();
        }

        private void BuildTendrils()
        {
            _tendrils = new SpriteRenderer[TENDRIL_COUNT];
            for (int i = 0; i < TENDRIL_COUNT; i++)
            {
                // Base-pivoted, so localScale.y IS the height above the floor and nothing
                // else has to move in lockstep with it.
                var sr = Opaque($"Tendril{i}", RootSprites.Tendril, _palette.Bark,
                                ORDER_TENDRIL, transform);
                float angle = (i / (float)TENDRIL_COUNT) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                float radius = Random.Range(0.35f, 0.72f);
                sr.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius,
                                                         Mathf.Sin(angle) * radius * 0.34f, 0f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-26f, 26f));
                sr.flipX = Random.value < 0.5f;
                _tendrils[i] = sr;
            }
        }

        /// <summary>
        /// The heap the creature comes up through, and the chips it throws when it clears the
        /// surface. One family of objects doing both jobs, because they are literally the same
        /// earth — a separate "concealer" would be a second thing to keep in step.
        /// </summary>
        private void BuildMound()
        {
            _clods = new SpriteRenderer[CLOD_COUNT];
            _clodRest = new Vector3[CLOD_COUNT];
            _clodVelocity = new Vector2[CLOD_COUNT];

            for (int i = 0; i < CLOD_COUNT; i++)
            {
                var sr = Opaque($"Clod{i}", RootSprites.Clod, _palette.Soil, ORDER_CLOD, transform);
                float spread = Mathf.Lerp(-0.72f, 0.72f, (i + 0.5f) / CLOD_COUNT)
                             + Random.Range(-0.08f, 0.08f);
                _clodRest[i] = new Vector3(spread, Random.Range(-0.06f, 0.16f), 0f);
                sr.transform.localPosition = _clodRest[i];
                sr.transform.localScale = Vector3.one * Random.Range(0.34f, 0.58f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                float away = Mathf.Sign(spread == 0f ? 1f : spread);
                _clodVelocity[i] = new Vector2(away * Random.Range(1.1f, 2.9f),
                                               Random.Range(1.8f, 3.6f));
            }
        }

        private void BuildMotes()
        {
            _motes = new SpriteRenderer[MOTE_COUNT];
            _moteDrift = new Vector3[MOTE_COUNT];
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                var sr = Additive($"Mote{i}", ElementalSprites.Sparkle, _palette.Sap,
                                  ORDER_MOTE, transform);
                sr.transform.localPosition = new Vector3(Random.Range(-0.7f, 0.7f),
                                                         Random.Range(-0.1f, 0.35f), 0f);
                sr.transform.localScale = Vector3.one * Random.Range(0.09f, 0.18f);
                _moteDrift[i] = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.7f, 1.7f), 0f);
                _motes[i] = sr;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("RiseLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. The wrong literal here is
                // what once left every placed torch a cookie-less Sprite light.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.Sap);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 3.1f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; Destroy(go); }
        }

        private SpriteRenderer Additive(string objectName, Sprite sprite, Color color, int order,
                                        Transform parent)
            => Make(objectName, sprite, color, order, parent, ElementalSprites.SharedAdditiveMaterial);

        private SpriteRenderer Opaque(string objectName, Sprite sprite, Color color, int order,
                                      Transform parent)
            => Make(objectName, sprite, color, order, parent, ElementalSprites.SharedUnlitMaterial);

        private static SpriteRenderer Make(string objectName, Sprite sprite, Color color, int order,
                                           Transform parent, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            sr.sharedMaterial = material;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
