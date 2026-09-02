using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The shell itself: the Fresnel rim, the interior tint, the travelling sheen, and the
    /// lattice of hexagonal facets that gives the sphere a surface.
    /// </summary>
    internal sealed partial class ShieldSphereFX
    {
        /// <summary>
        /// How far a facet's centre sits inside the nominal radius. Slightly inside, so the rim
        /// ring reads as the outermost thing and the facets never poke through it.
        /// </summary>
        private const float FACET_SHELL = 0.965f;

        /// <summary>
        /// Hexagon half-width per unit of sphere radius. Derived rather than guessed: 30 cells
        /// tiling a sphere of radius r each cover 4*pi*r^2/30, and a regular hexagon of
        /// half-width h has area 2.598*h^2, which gives h ~ 0.40r. The sprite's hexagon is
        /// inscribed at 0.88 of the half-texture, so the scale that yields that half-width is
        /// 0.40 / 0.44 ~ 0.91. Rounded down a little: at exactly the tiling value the cells
        /// touch on every side and the mesh reads as a solid, which defeats the point of
        /// drawing them hollow.
        /// </summary>
        private const float FACET_SIZE_PER_RADIUS = 0.86f;

        private void BuildShell()
        {
            // Interior tint, behind the character: what makes them read as INSIDE the sphere
            // rather than standing in front of a ring. Kept very faint — it is additive, and
            // anything stronger washes out the silhouette it is supposed to frame.
            _fill = MakeSprite("Fill", ElementalSprites.Glow, _root, FILL_ORDER);
            _fill.transform.localPosition = _config.BodyOffset;
            _fill.transform.localScale = Vector3.one * _config.Radius * 2f;
            SetColor(_fill, _config.Palette.Edge, 0f);

            // The Fresnel edge. Pinned to the authored radius through the ring's own measured
            // band position, so the drawn boundary IS the sphere at any size.
            _rim = MakeSprite("Rim", ElementalSprites.Ring, _root, RIM_ORDER);
            _rim.transform.localPosition = _config.BodyOffset;
            SetColor(_rim, _config.Palette.Mid, 0f);

            _sheen = MakeSprite("Sheen", ShieldSprites.Sheen, _root, SHEEN_ORDER);
            SetColor(_sheen, _config.Palette.Core, 0f);

            // The contact flash lives on its own root so it can be moved to wherever the shell
            // was struck without disturbing anything else.
            _flash = MakeSprite("ContactFlash", ElementalSprites.Halo, _root, FLASH_ORDER);
            _flashRoot = _flash.transform;
            SetColor(_flash, _config.Palette.Core, 0f);

            BuildFacets();
        }

        /// <summary>
        /// Place the cells on a Fibonacci lattice — the standard way to spread N points evenly
        /// over a sphere. Evenness matters here: a lattice from random directions clumps, and a
        /// clump on a shell reads as damage rather than as construction.
        /// </summary>
        private void BuildFacets()
        {
            float size = _config.Radius * FACET_SIZE_PER_RADIUS;
            const float goldenAngle = 2.39996323f;   // pi * (3 - sqrt 5)

            for (int i = 0; i < FACET_COUNT; i++)
            {
                float y = 1f - (i / (FACET_COUNT - 1f)) * 2f;
                float ringRadius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float theta = goldenAngle * i;

                var facet = new Facet
                {
                    LatticeDirection = new Vector3(
                        Mathf.Cos(theta) * ringRadius, y, Mathf.Sin(theta) * ringRadius),
                    // Uneven resting brightness, so the mesh has texture standing still. All
                    // of them at one value reads as a printed pattern.
                    RestAlpha = Range(0.16f, 0.42f),
                    Size = size * Range(0.86f, 1.10f),
                    BreakSpeed = Range(1.1f, 2.6f),
                    BreakSpin = Range(-220f, 220f),
                };

                facet.Renderer = MakeSprite("Facet" + i, ShieldSprites.Facet, _root, BACK_ORDER);
                facet.Root = facet.Renderer.transform;
                SetColor(facet.Renderer, _config.Palette.Mid, 0f);
                _facets.Add(facet);
            }
        }

        /// <summary>
        /// Lay every facet back onto the sphere for this frame.
        ///
        /// <para>Three things happen per cell and each is load-bearing. It is FORESHORTENED
        /// along its own radial axis by <c>|d.z|</c>, which is what makes a flat quad sit on a
        /// curve — without it the hexagons stay square-on all the way to the silhouette and the
        /// shell reads as a sticker sheet. It is lit by a FRESNEL term, brightest where the
        /// surface turns away from the camera, which is what a transparent shell actually does
        /// and is why the sphere has a bright edge and a see-through middle. And it is SORTED
        /// by the sign of <c>d.z</c>, which is the only statement in the whole rig that the
        /// character is inside something.</para>
        /// </summary>
        private void UpdateFacets(float envelope, float assemble, float breakTime)
        {
            _shellRotation = Quaternion.AngleAxis(_age * _shellSpeed, _shellAxis);

            for (int i = 0; i < _facets.Count; i++)
            {
                var facet = _facets[i];
                if (facet.Root == null) continue;

                Vector3 d = _shellRotation * facet.LatticeDirection;

                float shell = FACET_SHELL * assemble + facet.BreakSpeed * breakTime;
                Vector3 local = _config.BodyOffset +
                                new Vector3(d.x, d.y, 0f) * (_config.Radius * shell);
                facet.Root.localPosition = local;

                // Local X is the radial axis of the sprite, so aiming it outward is what makes
                // the non-uniform scale below compress the cell against the curve rather than
                // shearing it sideways.
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                facet.Root.localRotation = Quaternion.Euler(0f, 0f,
                    angle + facet.BreakSpin * breakTime);

                float foreshorten = Mathf.Max(0.16f, Mathf.Abs(d.z));
                float grow = assemble * (1f + breakTime * 0.6f);
                facet.Root.localScale = new Vector3(
                    facet.Size * foreshorten * grow, facet.Size * grow, 1f);

                // 0 facing the camera, 1 at the silhouette.
                float fresnel = 1f - Mathf.Abs(d.z);
                float lit = 0.16f + 0.84f * Mathf.Pow(fresnel, 1.7f);

                bool inFront = d.z >= 0f;
                if (inFront != facet.InFront)
                {
                    facet.InFront = inFront;
                    facet.Renderer.sortingOrder = _baseOrder + (inFront ? FRONT_ORDER : BACK_ORDER);
                }
                // The far wall is seen THROUGH the near one, so it is dimmer. Without this the
                // two hemispheres are indistinguishable and the sphere looks like a flat ring
                // of cells whichever way it turns.
                float depthFade = inFront ? 1f : 0.42f;

                float ripple = RippleAt(d);
                float alpha = (facet.RestAlpha * lit * depthFade + ripple) * envelope;
                alpha *= 1f - breakTime;

                // Struck cells go SOLID. A hollow cell cannot get much brighter than its own
                // border; filling it is what makes a blocked hit land as a slab of light.
                var wanted = ripple > 0.30f ? ShieldSprites.FacetSolid : ShieldSprites.Facet;
                if (facet.Renderer.sprite != wanted) facet.Renderer.sprite = wanted;

                SetColor(facet.Renderer,
                    Color.Lerp(_config.Palette.Mid, _config.Palette.Core, Mathf.Clamp01(ripple * 1.6f)),
                    alpha);
            }
        }

        private void UpdateRim(float envelope, float assemble, float breakTime, float impact)
        {
            if (_rim != null)
            {
                float radius = _config.Radius * (assemble + breakTime * 0.55f);
                float scale = radius / RING_BAND_RADIUS;
                _rim.transform.localScale = new Vector3(scale, scale, 1f);

                // The rim breathes slightly out of phase with the shell's rotation, so the two
                // never resolve into one period the eye can lock onto.
                float breathe = 1f + 0.035f * Mathf.Sin(_age * 2.1f);
                _rim.transform.localScale *= breathe;

                float alpha = (0.55f + 0.15f * Mathf.Sin(_age * 3.3f) + impact * 0.8f) * envelope;
                // Breaking, the rim FLARES before it goes. The last thing a barrier does is
                // give — a rim that simply dims looks like the effect was switched off.
                alpha *= breakTime <= 0f
                    ? 1f
                    : (breakTime < 0.25f
                        ? Mathf.Lerp(1f, 1.9f, breakTime / 0.25f)
                        : Mathf.Lerp(1.9f, 0f, (breakTime - 0.25f) / 0.75f));
                SetColor(_rim, Color.Lerp(_config.Palette.Mid, _config.Palette.Core,
                    Mathf.Max(impact, breakTime * 2f)), alpha);
            }

            if (_fill != null)
            {
                _fill.transform.localScale = Vector3.one * _config.Radius * 2f * assemble;
                SetAlpha(_fill, (0.13f + 0.05f * Mathf.Sin(_age * 1.7f) + impact * 0.30f)
                                * envelope * (1f - breakTime));
            }
        }

        /// <summary>
        /// A highlight sliding across the front of the sphere. Travels on its own slow period,
        /// fading out at the edges of its sweep so it never visibly wraps — light on glass
        /// arrives and leaves, it does not orbit.
        /// </summary>
        private void UpdateSheen(float envelope, float assemble)
        {
            if (_sheen == null) return;

            const float sweepPeriod = 3.4f;
            float t = Mathf.Repeat(_age / sweepPeriod, 1f);
            float across = Mathf.Lerp(-0.85f, 0.85f, t);

            float radius = _config.Radius * assemble;
            _sheen.transform.localPosition = _config.BodyOffset + new Vector3(across * radius, 0f, 0f);
            _sheen.transform.localScale = new Vector3(radius * 0.55f, radius * 1.75f, 1f);
            // Tilted, so it reads as a band wrapping the sphere rather than a bar across it.
            _sheen.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            // Brightest crossing the middle, gone at either edge.
            float presence = Mathf.Sin(t * Mathf.PI);
            SetAlpha(_sheen, Mathf.Pow(presence, 2.2f) * 0.42f * envelope);
        }
    }
}
