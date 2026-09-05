using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The third beat of a leap, and the one the budget goes to: coming down.
    ///
    /// <para>THE RING IS A PROMISE, SO IT IS PINNED (L5). Every <c>ElementalSprites</c> sprite
    /// is exactly one world unit and <c>Ring</c>'s bright band peaks at normalized radius 0.78,
    /// so a boundary meant to sit at world radius r is scaled <c>r / 0.39</c>. Getting that
    /// wrong is invisible in code and brutal on screen — the arcane flame once drew its only
    /// hard contour at 1.51 u against a 2.5 u damage circle, so 46 % of the ground that hurt
    /// carried no readable pixel. Here the drawn edge and <c>SpellDefinition.radius</c> are the
    /// same number by construction.</para>
    ///
    /// <para>The EARTH is the rig's opaque statement (L3): the thrown chips and the cracks left
    /// behind are the same material and both sit on
    /// <c>ElementalSprites.SharedUnlitMaterial</c>, while the ring, the dust and the light are
    /// additive. One opaque family is what separates "the world was hit" from "something was
    /// lit"; the vortex and the ki aura record the identical rule for their own ground debris.
    /// The cracks are the only part on <c>LAYER_FLOOR_DECALS</c> — a decal that draws in front
    /// of the character standing on it is not a mark on the ground.</para>
    ///
    /// <para>NO ZOOM PUNCH. The camera beat and the hit-stop are fired by the executor, which
    /// is where the damage is; there is no seam-legal zoom punch in a 16 PPU game because
    /// <c>CameraPixelSnap</c> derives its lattice from the live ortho size.</para>
    /// </summary>
    internal sealed partial class LeapImpactFX : MonoBehaviour
    {
        /// <summary>How long the fissures stay scratched into the floor.</summary>
        private const float CRACK_SECONDS = 2.2f;

        /// <summary>How long the bright half of the impact lasts.</summary>
        private const float FLASH_SECONDS = 0.55f;

        private const int CLOD_COUNT = 14;
        private const int DUST_COUNT = 16;
        private const int CRACK_COUNT = 7;

        private const int ORDER_RING  = 43;
        private const int ORDER_DUST  = 45;
        private const int ORDER_CLOD  = 49;
        private const int ORDER_CRACK = 222;   // FloorDecals

        private RootPalette _palette;
        private float _radius;
        private float _age;

        private Transform _groundPlane;
        private SpriteRenderer _ring;
        private SpriteRenderer _flash;
        private SpriteRenderer[] _cracks;
        private float[] _crackLength;
        private SpriteRenderer[] _dust;
        private Vector2[] _dustVelocity;
        private SpriteRenderer[] _clods;
        private Vector2[] _clodVelocity;
        private Vector2[] _clodGround;
        private float[] _clodHeight;
        private float[] _clodRise;
        private Component _light;

        /// <summary>
        /// Slam the ground at <paramref name="position"/> with the spell's own radius. Refused
        /// outside Play Mode: the sequence advances from Update, which never runs there.
        /// </summary>
        public static void Play(Vector2 position, SpellDefinition spell)
        {
            if (!Application.isPlaying) return;

            var go = new GameObject("LeapImpactFX");
            go.transform.position = position;

            var fx = go.AddComponent<LeapImpactFX>();
            fx._radius = spell != null && spell.radius > 0f ? spell.radius : 2f;
            fx._palette = RootPalette.From(spell != null ? spell.particleColor : Color.white);
            fx.Build();
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void Build()
        {
            ElementalSprites.EnsureAll();
            RootSprites.EnsureAll();

            // ONE squash parent for everything lying on the floor, rotation on the children.
            // Squashing each item individually foreshortens its length without turning its
            // direction, and it slides across the ground instead of lying on it.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localScale = new Vector3(1f, 0.42f, 1f);
            _groundPlane = plane.transform;

            _ring = Additive("ImpactRing", ElementalSprites.Ring, _palette.Leaf, ORDER_RING, _groundPlane);
            _flash = Additive("ImpactFlash", RootSprites.Burst, _palette.Sap, ORDER_DUST, transform);
            _flash.transform.localScale = Vector3.one * (_radius * 0.9f);

            BuildCracks();
            BuildDust();
            BuildClods();
            BuildLight();
        }

        private void BuildCracks()
        {
            _cracks = new SpriteRenderer[CRACK_COUNT];
            _crackLength = new float[CRACK_COUNT];

            for (int i = 0; i < CRACK_COUNT; i++)
            {
                // RootSprites.Crack runs along +X from its pivot, so a Z rotation aims it
                // outward and no offset is needed.
                var sr = Opaque($"Crack{i}", RootSprites.Crack, _palette.Soil, ORDER_CRACK, _groundPlane);
                sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;

                float angle = (i / (float)CRACK_COUNT) * 360f + Random.Range(-14f, 14f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                _crackLength[i] = _radius * Random.Range(0.55f, 1.0f);
                sr.transform.localScale = new Vector3(0.01f, Random.Range(0.16f, 0.30f), 1f);
                _cracks[i] = sr;
            }
        }

        private void BuildDust()
        {
            _dust = new SpriteRenderer[DUST_COUNT];
            _dustVelocity = new Vector2[DUST_COUNT];

            for (int i = 0; i < DUST_COUNT; i++)
            {
                var sr = Additive($"Dust{i}", ElementalSprites.Wisp, _palette.Bark, ORDER_DUST, _groundPlane);
                float angle = (i / (float)DUST_COUNT) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                sr.transform.localPosition = Vector3.zero;
                sr.transform.localScale = Vector3.one * Random.Range(0.22f, 0.46f);
                // Sized off the radius, so a bigger slam throws its dust further rather than
                // throwing the same puff over a bigger circle.
                _dustVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                                 * (_radius * Random.Range(1.6f, 3.1f));
                _dust[i] = sr;
            }
        }

        private void BuildClods()
        {
            _clods = new SpriteRenderer[CLOD_COUNT];
            _clodVelocity = new Vector2[CLOD_COUNT];
            _clodGround = new Vector2[CLOD_COUNT];
            _clodHeight = new float[CLOD_COUNT];
            _clodRise = new float[CLOD_COUNT];

            for (int i = 0; i < CLOD_COUNT; i++)
            {
                // NOT under the ground plane: a chip that has been thrown into the air is no
                // longer lying on the floor, and squashing it would flatten the one layer that
                // is supposed to leave it.
                var sr = Opaque($"Clod{i}", RootSprites.Clod, _palette.Soil, ORDER_CLOD, transform);
                sr.transform.localScale = Vector3.one * Random.Range(0.16f, 0.34f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                float angle = Random.Range(0f, Mathf.PI * 2f);
                _clodVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.45f)
                                 * (_radius * Random.Range(0.9f, 2.0f));
                _clodRise[i] = Random.Range(2.4f, 5.2f);
                _clods[i] = sr;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("ImpactLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.Sap);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.9f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.15f);
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
            sr.color = new Color(color.r, color.g, color.b, 0f);
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
