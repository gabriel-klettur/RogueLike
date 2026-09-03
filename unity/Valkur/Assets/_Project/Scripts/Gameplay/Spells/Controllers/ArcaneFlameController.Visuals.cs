using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The SPRITE half of the arcane flame: the scorch it burns into the floor, the two
    /// additive beds that are the light the fire casts on that floor, the boundary rings, and
    /// the haze the flames stand in. The fire itself is in
    /// <c>ArcaneFlameController.Fire.cs</c>; the envelope that drives both is in
    /// <c>ArcaneFlameController.Animation.cs</c>.
    ///
    /// <para>WHAT CHANGED, AND WHY. This used to be six concentric additive discs plus two
    /// clouds of drifting motes — a glowing magic circle, not a fire. Every layer was radial
    /// and every layer was still, so the shape the eye read was a DISC that happened to be
    /// bright, and the only motion in it was a rune turning. Fire is not a bright disc: it is a
    /// crowd of short, narrow, upward silhouettes whose OUTLINE flickers, standing on ground
    /// that glows because they are burning on it. That flickering outline is the one thing
    /// separating fire from a lamp — <c>KiAuraFX</c> records the same fact for the ki charge,
    /// and <c>FlameConeFX</c> is where this rig's gradient and material recipe come from.</para>
    ///
    /// <para>THE FOOTPRINT IS NOT SQUASHED. Every other ground-plane layer in the project is
    /// (<c>VortexFunnelFX</c>'s ground plane sits at 0.34 on Y), because those effects stand UP
    /// in the air and the squash is what says the circle is lying on the floor beneath them.
    /// This one has nothing standing up in it: the flames are knee-high and the disc IS the
    /// effect, so an ellipse inside a round boundary ring would only disagree with the one
    /// thing that ring promises. The fire is grounded by what is UNDER it — the scorch and the
    /// two beds — rather than by foreshortening.</para>
    /// </summary>
    public partial class ArcaneFlameController
    {
        private const float BoundaryRingLife = 0.34f;
        private const int   MaxBoundaryRings = 4;

        /// <summary>
        /// The boundary's hue. A saturated magenta-violet rather than the palette's near-white
        /// `accent`, so the whole silhouette reads arcane and the only near-white pixels in the
        /// effect are the roots of the flames. Slightly hotter than <c>palette.glow</c> so the
        /// rim still separates from the fire behind it.
        /// </summary>
        private static readonly Color RingColor = new Color(0.86f, 0.42f, 1.00f);

        private SpriteRenderer _scorch, _runeSpin, _runeStatic, _groundGlow, _groundHot, _haze;

        private readonly List<BoundaryRing> _rings = new List<BoundaryRing>(MaxBoundaryRings);
        /// <summary>Expired boundary rings, kept for the next tick instead of destroyed.</summary>
        private readonly List<SpriteRenderer> _ringPool = new List<SpriteRenderer>(MaxBoundaryRings);

        private struct BoundaryRing
        {
            public SpriteRenderer Sr;
            public float Age;
        }

        // ── Build ───────────────────────────────────────────────────────────────

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();

            float d = _radius * 2f;   // a child's localScale is its world DIAMETER

            // Ground. FloorDecals is inside the ambient light mask, but this uses the unlit
            // material like the rest of the elemental family — ground burning through should
            // not dim with the daylight.
            _scorch = MakeChild("Scorch", ElementalSprites.Glow,
                new Color(0.09f, 0.02f, 0.14f, 0.42f), d * ScorchRadiusMul,
                SortingConfig.LAYER_FLOOR_DECALS, 48, additive: false);

            // The two beds are the LIGHT THE FIRE CASTS ON THE GROUND, and they are what
            // grounds the effect now that the interior is particles. Two rather than one
            // because a burning patch is brighter where the flames are denser, and a single
            // flat disc reads as a decal again.
            _groundHot = MakeChild("GroundHot", ElementalSprites.Glow,
                WithAlpha(_palette.core, 0.46f), d * GroundHotRadiusMul,
                SortingConfig.LAYER_VFX, 2, additive: true);
            _groundGlow = MakeChild("GroundGlow", ElementalSprites.Glow,
                WithAlpha(_palette.glow, 0.38f), d * GroundGlowRadiusMul,
                SortingConfig.LAYER_VFX, 3, additive: true);

            // THE BOUNDARY. Crest on _radius exactly — see invariant 1 in the main file.
            //
            // ON LAYER_VFX, NOT ON THE FLOOR, and this is a gameplay decision rather than an
            // art one. The scorch above is a ground mark and being occluded by a wall is
            // correct for it. This ring is the only thing that tells the player where the
            // damage stops — and measured in the shipped town, tree `Canopy` renderers sit on
            // WallsTop (sorting value 8) and building `Footprint` on WallsBottom (5), both far
            // above FloorDecals (3). On the floor the ring came out as a CRESCENT, its right
            // half swallowed by a building, which recreates dynamically the exact failure the
            // crest fix removed: a hazard edge with no readable pixel.
            //
            // Tinted with RingColor, NOT the palette's `accent`. Accent is a pale lilac-white
            // (0.95, 0.85, 1.00) and TWO ring layers stack, so on screen the boundary
            // composited to near-white and became the loudest thing in the effect.
            float ringDiameter = d / RingCrestNormalized;
            _runeStatic = MakeChild("RuneStatic", ElementalSprites.Ring,
                WithAlpha(RingColor, 0.20f), ringDiameter,
                SortingConfig.LAYER_VFX, 4, additive: false);
            // Dimmer than it was. The ring used to be the loudest thing in the effect
            // because the interior had nothing in it; now the fire carries the shape and the
            // boundary only has to stay READABLE, which is a much lower bar than dominant.
            _runeSpin = MakeChild("Rune", ElementalSprites.Ring,
                WithAlpha(RingColor, 0.50f), ringDiameter,
                SortingConfig.LAYER_VFX, 5, additive: false);

            // The one alpha-blended sprite up here: it is the MASS the fire stands in.
            // Additive alone has no body, it only ever brightens, so without this the flames
            // have nothing to be inside of.
            _haze = MakeChild("Haze", ElementalSprites.Halo,
                new Color(0.30f, 0.12f, 0.55f, 0.20f), d * HazeRadiusMul,
                SortingConfig.LAYER_VFX, 8, additive: false);

            BuildFire();
        }

        private SpriteRenderer MakeChild(string name, Sprite sprite, Color color, float scale,
            string layer, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            // sharedMaterial, never material: `.material` clones per renderer and leaks an
            // instance that EditMode tests report. Neither material is ever mutated through
            // this reference — that is the AuraController.cs:262 landmine.
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
