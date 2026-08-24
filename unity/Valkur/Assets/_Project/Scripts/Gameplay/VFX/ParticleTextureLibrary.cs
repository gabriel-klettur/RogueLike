using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Builds and caches the procedural billboard textures used by <see cref="ParticleEmitter"/>.
    ///
    /// Valkur ships no particle sprite art: every shape in <see cref="ParticleTextureShape"/> is
    /// generated once at runtime into a small RGBA texture whose alpha channel carries the shape
    /// and whose RGB carries luminance, so the preset's own colour gradient does all the tinting.
    /// Every radial shape leaves that luminance at pure white; only <see cref="ParticleTextureShape.Leaf"/>
    /// spends it, on a midrib that has to survive being multiplied by a red autumn tint as
    /// readily as by a green spring one.
    ///
    /// Textures are keyed by (shape, quantised softness) and marked <see cref="HideFlags.DontSave"/>
    /// so they never leak into a scene or an EditMode test fixture.
    /// </summary>
    public static class ParticleTextureLibrary
    {
        /// <summary>Edge length of every generated texture. 128 is ample for a billboard.</summary>
        private const int RESOLUTION = 128;

        /// <summary>Softness is quantised to this many steps before keying the cache.</summary>
        private const int SOFTNESS_STEPS = 16;

        // ---- Vortex ----
        /// <summary>Radians of turn per e-fold of radius — the spiral's pitch. Above ~4 the
        /// arms wrap tightly enough to read as rings; below ~1.5 as a straight two-blade
        /// propeller.</summary>
        private const float VORTEX_TWIST = 2.8f;

        /// <summary>Arm count. Two reads as a spiral at a glance; four reads as a fan.</summary>
        private const int VORTEX_ARMS = 2;

        /// <summary>Radius of the bright hub the arms converge into, which is also the disc
        /// where the spiral's pitch has gone too fine for 128 px to resolve.</summary>
        private const float VORTEX_HUB = 0.16f;

        /// <summary>Peak alpha of that hub. Deliberately short of opaque: a Vortex is drawn
        /// OVER painted portal art, and a solid white core erases the mouth it is supposed
        /// to be turning inside.</summary>
        private const float VORTEX_HUB_ALPHA = 0.55f;

        /// <summary>Floor on r before the log. Inside this the arm phase spins arbitrarily
        /// fast; the hub covers all of it.</summary>
        private const float VORTEX_MIN_R = 0.04f;

        /// <summary>Wash held between the arms so the shape stays one disc.</summary>
        private const float VORTEX_FILL = 0.12f;

        /// <summary>Radius where the disc starts fading out. Held flat until here and faded
        /// to nothing by r = 1, so the quad has no visible edge; a plain (1-r)^k falloff
        /// instead crushes the arms everywhere they are actually legible.</summary>
        private const float VORTEX_RIM_START = 0.5f;

        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        // Domain Reload is OFF — runtime-created textures are destroyed when leaving play mode,
        // which would leave the static cache holding dead references on the next Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cache.Clear();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Resolves <see cref="ParticleTextureShape.Auto"/> into a concrete shape from the
        /// preset's <paramref name="kind"/> and blend mode. Non-Auto values pass through.
        /// </summary>
        public static ParticleTextureShape ResolveShape(ParticleTextureShape requested, string kind, bool additive)
        {
            if (requested != ParticleTextureShape.Auto) return requested;

            switch (kind ?? "")
            {
                case "smoke":
                case "smoke_emitter":
                case "smoke_burst":
                    return ParticleTextureShape.Smoke;

                case "slash":
                case "dash":
                case "firework":
                    return ParticleTextureShape.Spark;

                case "portal":
                    return additive ? ParticleTextureShape.Glow : ParticleTextureShape.SoftDot;

                case "aura":
                case "healing_aura":
                case "arcane_flame":
                    return ParticleTextureShape.Glow;

                // falling_leaf still resolves to SoftDot ON PURPOSE, now that Leaf exists.
                // Auto is the serialized default, so re-pointing this one case would change
                // the silhouette of every preset that never chose a shape — one edit, 131
                // presets, no diff to read. A leaf preset opts in with textureShape = Leaf.
                case "falling_leaf":
                case "water_flow":
                case "water_fountain":
                    return ParticleTextureShape.SoftDot;

                default:
                    return additive ? ParticleTextureShape.Glow : ParticleTextureShape.SoftDot;
            }
        }

        /// <summary>
        /// Returns the cached texture for a shape, generating it on first request.
        /// Returns <c>null</c> for <see cref="ParticleTextureShape.None"/> (legacy untextured quad)
        /// and for <see cref="ParticleTextureShape.Auto"/> — resolve Auto first.
        /// </summary>
        public static Texture2D Get(ParticleTextureShape shape, float softness)
        {
            if (shape == ParticleTextureShape.None || shape == ParticleTextureShape.Auto)
                return null;

            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(softness) * SOFTNESS_STEPS), 0, SOFTNESS_STEPS);
            int key = ((int)shape * (SOFTNESS_STEPS + 1)) + step;

            // Unity's overloaded null also catches a texture destroyed by a play-mode exit.
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = Generate(shape, (float)step / SOFTNESS_STEPS);
            _cache[key] = tex;
            return tex;
        }

        // ------------------------------------------------------------------ generation

        private static Texture2D Generate(ParticleTextureShape shape, float softness)
        {
            // Leaf and Petal are authored sprites on a logical texel grid, so they need the
            // sampler to leave their blocks alone: bilinear would ramp every border back into
            // a gradient, and the mip chain would average the three flat tones into one. Every
            // other shape is a radial falloff that wants both.
            bool pixelArt = shape == ParticleTextureShape.Leaf || shape == ParticleTextureShape.Petal;

            var tex = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32,
                                    mipChain: !pixelArt, linear: false)
            {
                name = $"ParticleTex_{shape}_{softness:0.00}",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = pixelArt ? FilterMode.Point : FilterMode.Bilinear,
                anisoLevel = 0,
            };

            var pixels = new Color32[RESOLUTION * RESOLUTION];
            float inv = 2f / (RESOLUTION - 1);

            for (int y = 0; y < RESOLUTION; y++)
            {
                float ny = (y * inv) - 1f; // -1 .. 1
                for (int x = 0; x < RESOLUTION; x++)
                {
                    float nx = (x * inv) - 1f;
                    float alpha = Mathf.Clamp01(EvaluateAlpha(shape, nx, ny, softness));

                    // Straight alpha with greyscale RGB — never premultiplied, because
                    // ParticleMaterialCache keeps _ALPHAPREMULTIPLY_ON off. Every shape that
                    // predates Leaf returns luminance 1 here, so this writes the historical
                    // solid-white RGB byte for byte.
                    byte lum = (byte)(Mathf.Clamp01(EvaluateLuminance(shape, nx, ny, softness)) * 255f);
                    pixels[(y * RESOLUTION) + x] = new Color32(lum, lum, lum, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: !pixelArt, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>
        /// Alpha of the shape at normalised coordinates in [-1, 1]. This is the pure shape
        /// function used to fill the texture; it is public so tests and tooling can assert
        /// the falloff without a GPU read-back (generated textures are upload-only).
        /// </summary>
        public static float EvaluateAlpha(ParticleTextureShape shape, float nx, float ny, float softness)
        {
            float r = Mathf.Sqrt((nx * nx) + (ny * ny));

            // The unit-circle early-out is a radial convenience for radial shapes. It has to
            // skip the pixel-art ones: it tests the CONTINUOUS position, so it slices a smooth
            // arc through whichever logical cells reach past r = 1 and hands back a rounded,
            // half-eaten block — quantisation undone at exactly the silhouette's edge. Their
            // own width functions already close well inside the quad.
            bool pixelArt = shape == ParticleTextureShape.Leaf || shape == ParticleTextureShape.Petal;
            if (r >= 1f && shape != ParticleTextureShape.Star && !pixelArt) return 0f;

            switch (shape)
            {
                case ParticleTextureShape.SoftDot:
                    return Mathf.Pow(1f - r, Mathf.Lerp(8f, 1.2f, softness));

                case ParticleTextureShape.Glow:
                {
                    float skirt = Mathf.Pow(1f - r, Mathf.Lerp(4f, 1f, softness));
                    float core = 1f - Mathf.SmoothStep(0f, 0.18f, r);
                    return skirt + (core * 0.9f);
                }

                case ParticleTextureShape.Spark:
                    return Mathf.Pow(1f - r, Mathf.Lerp(16f, 6f, softness));

                case ParticleTextureShape.Smoke:
                {
                    float mask = Mathf.Pow(1f - r, Mathf.Lerp(3f, 1.1f, softness));
                    float noise = FractalNoise(nx, ny);
                    return mask * Mathf.Lerp(0.55f, 1f, noise);
                }

                case ParticleTextureShape.Ring:
                {
                    const float RADIUS = 0.72f;
                    float width = Mathf.Lerp(0.06f, 0.28f, softness);
                    float d = (r - RADIUS) / width;
                    return Mathf.Exp(-d * d);
                }

                case ParticleTextureShape.Star:
                {
                    float core = Mathf.Pow(Mathf.Max(0f, 1f - r), Mathf.Lerp(10f, 4f, softness));
                    float armWidth = Mathf.Lerp(0.015f, 0.07f, softness);
                    float horizontal = Streak(ny, nx, armWidth);
                    float vertical = Streak(nx, ny, armWidth);
                    return core + (horizontal + vertical) * 0.85f;
                }

                case ParticleTextureShape.Vortex:
                {
                    // Logarithmic spiral: the arms are the loci of constant
                    // (angle - TWIST x ln r), which is the curve that keeps the same pitch at
                    // every radius. An Archimedean spiral (angle proportional to r) crowds its
                    // turns near the rim and reads as concentric rings once the quad is spun.
                    //
                    // r is floored before the log because the pitch is infinite at the centre:
                    // below a few percent of the radius the arms alias into noise no
                    // resolution fixes. The hub covers exactly that disc, so the floor is
                    // never visible.
                    float rr = Mathf.Max(r, VORTEX_MIN_R);
                    float phase = Mathf.Atan2(ny, nx) - (VORTEX_TWIST * Mathf.Log(rr));
                    float band = 0.5f + (0.5f * Mathf.Cos(VORTEX_ARMS * phase));

                    // Softness is arm WIDTH here, not blur: a cosine raised to a high power
                    // is a narrow ridge, to a low one a broad swell.
                    float arms = Mathf.Pow(band, Mathf.Lerp(4.5f, 1.5f, softness));

                    // Flat out to VORTEX_RIM_START, then a smoothstep to nothing at the quad
                    // edge. Note Mathf.SmoothStep(a, b, t) INTERPOLATES between a and b — it is
                    // not GLSL's smoothstep — so the ramp has to be built on a normalised t.
                    float rim = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((r - VORTEX_RIM_START) / (1f - VORTEX_RIM_START)));
                    float hub = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / VORTEX_HUB));

                    // The gaps between the arms keep a faint wash rather than going clear:
                    // an additive spiral over a dark portal mouth with transparent gaps reads
                    // as two detached comma shapes instead of as a turning disc.
                    return ((arms + VORTEX_FILL) * rim) + (hub * VORTEX_HUB_ALPHA);
                }

                case ParticleTextureShape.Leaf:
                {
                    // A FLECK, not a botanical illustration. The world is 16-PPU pixel art and
                    // one of these covers roughly 6-10 art texels; a smooth ovate outline with a
                    // midrib reads as an HD sprite pasted onto that world, which is exactly what
                    // the first version of this shape got wrong. What sells "leaf" here is the
                    // MOTION — turnoverCycles putting it edge-on, the spin, the drifting fall —
                    // so the texture's whole job is to be a small, hard-edged, shaded chip.
                    //
                    // Sampled at the CENTRE of a logical texel cell and returned as binary
                    // coverage: no partial alpha anywhere, because a ramp at the border is the
                    // thing that turns an authored-looking sprite back into an airbrushed blob.
                    PixelCell(nx, ny, LEAF_COLS, LEAF_ROWS, out float lcx, out float lcy);

                    // On a 5-column grid the only widths that exist are 1, 3 and 5 cells, so
                    // the width function's job is to CROSS those thresholds in the right rows,
                    // not to be a pretty curve. Biasing the ellipse's waist below centre and
                    // raising it to a power puts the broad part low and closes the top:
                    // bottom-to-top the chip runs 1, 5, 5, 3, 1 cells.
                    float lb = lcy + LEAF_WAIST_BIAS;
                    float lw = LEAF_HALF_W * Mathf.Pow(Mathf.Max(0f, 1f - (lb * lb)), LEAF_TAPER);
                    return Mathf.Abs(lcx) <= lw ? 1f : 0f;
                }

                case ParticleTextureShape.Petal:
                {
                    // Same idea, rounder and one cell shorter: broad and blunt at the outer
                    // edge, narrowing toward the base.
                    PixelCell(nx, ny, PETAL_COLS, PETAL_ROWS, out float pcx, out float pcy);

                    // Same threshold-crossing trick, waist biased the other way so the broad
                    // part sits high: bottom-to-top the chip runs 1, 3, 5, 3 cells, which is a
                    // blunt outer edge narrowing to a base.
                    float pb = pcy - PETAL_WAIST_BIAS;
                    float pw = PETAL_HALF_W * Mathf.Pow(Mathf.Max(0f, 1f - (pb * pb)), PETAL_TAPER);
                    return Mathf.Abs(pcx) <= pw ? 1f : 0f;
                }

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Greyscale value written into RGB at normalised coordinates in [-1, 1]. Public for
        /// the same reason as <see cref="EvaluateAlpha"/>: generated textures are uploaded with
        /// <c>makeNoLongerReadable</c>, so a pure function is the only way to assert them.
        ///
        /// Every shape except <see cref="ParticleTextureShape.Leaf"/> returns 1, which is the
        /// convention the library was built on — white RGB, all the colour from the preset's
        /// gradient. The leaf's midrib has to be a LUMINANCE feature for exactly that reason:
        /// startColor MULTIPLIES this texture, so a green vein would fight a red autumn tint,
        /// while a darker one reads as a vein whatever the leaf is tinted.
        /// </summary>
        public static float EvaluateLuminance(ParticleTextureShape shape, float nx, float ny, float softness)
        {
            bool pixelArt = shape == ParticleTextureShape.Leaf || shape == ParticleTextureShape.Petal;
            if (!pixelArt) return 1f;

            // THREE FLAT TONES, snapped to the same logical cell as the alpha. Flatness is the
            // whole point: a continuous ramp is what made the previous version read as a
            // rendered object rather than an authored sprite, and a pixel artist shading a
            // 5x8 chip reaches for three values, not a gradient.
            //
            // Luminance rather than colour, because startColor MULTIPLIES this texture — a
            // green-tinted shade would fight an autumn-red leaf, while a darker VALUE reads as
            // the same fold whatever the preset tints it.
            int cols = shape == ParticleTextureShape.Leaf ? LEAF_COLS : PETAL_COLS;
            int rows = shape == ParticleTextureShape.Leaf ? LEAF_ROWS : PETAL_ROWS;
            PixelCell(nx, ny, cols, rows, out float cx, out float cy);

            // Fixed key light from the upper left, so a spinning chip flashes as its lit face
            // swings past the camera instead of staying uniformly bright.
            float key = ((-cx + cy) * 0.5f) + 0.5f;                     // 0 lower-right .. 1 upper-left

            // Softness is the only knob left, and it drives CONTRAST, not blur: 0 is a hard
            // three-value chip, 1 flattens toward a single value. Blur is deliberately not on
            // offer here — it is what this shape exists to stop doing.
            float midTone   = Mathf.Lerp(0.72f, 0.88f, softness);
            float shadeTone = Mathf.Lerp(0.44f, 0.74f, softness);

            if (key > 0.60f) return 1f;
            return key > 0.34f ? midTone : shadeTone;
        }

        // ------------------------------------------------------------------ pixel-art grid
        //
        // Leaf and Petal are the only shapes here that are AUTHORED SPRITES rather than
        // radial falloffs. They are rasterised onto a logical texel grid and sampled at cell
        // centres, so the 128 px texture holds a handful of big hard-edged blocks. Sized so
        // one logical cell lands on roughly one 16-PPU art texel at the authored particle
        // size — that alignment is what makes them sit in the world instead of on top of it.
        //
        // Do not "smooth" these. The first version of both shapes was a signed-distance
        // outline with an anti-aliased edge and a Gaussian midrib; it was technically nicer
        // and looked like an HD illustration dropped onto pixel art.
        private const int LEAF_COLS = 5;
        private const int LEAF_ROWS = 5;
        private const float LEAF_HALF_W = 0.95f;
        // Bottom-to-top the leaf runs 3, 5, 5, 3, 1 cells: broad and blunt at the base,
        // closing to a point. An earlier tuning ended in a single bottom cell and the chip
        // grew a stem — at which point it reads as a tiny tree, not as something falling.
        private const float LEAF_WAIST_BIAS = 0.28f;
        private const float LEAF_TAPER = 1.2f;

        private const int PETAL_COLS = 5;
        private const int PETAL_ROWS = 4;
        private const float PETAL_HALF_W = 0.95f;
        // The petal runs 3, 5, 5, 3 — a rounded chip with no point at either end. Its bias is
        // deliberately zero: on a 4-row grid any non-zero value collapses one end to a single
        // cell, which reads as a stalk and turns the petal into a mushroom. What distinguishes
        // its two ends is the SHADING, not the outline.
        private const float PETAL_WAIST_BIAS = 0f;
        private const float PETAL_TAPER = 1f;

        /// <summary>
        /// Snaps a continuous sample to the centre of its logical texel cell. Every pixel-art
        /// shape evaluates from the CENTRE so that one cell resolves to exactly one value —
        /// sampling at the raw position would put a soft, position-dependent edge back inside
        /// each block, which is the whole thing this grid exists to prevent.
        /// </summary>
        private static void PixelCell(float nx, float ny, int cols, int rows, out float cx, out float cy)
        {
            int ix = Mathf.Clamp(Mathf.FloorToInt(((nx * 0.5f) + 0.5f) * cols), 0, cols - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt(((ny * 0.5f) + 0.5f) * rows), 0, rows - 1);
            cx = (((ix + 0.5f) / cols) * 2f) - 1f;
            cy = (((iy + 0.5f) / rows) * 2f) - 1f;
        }

        private static float Coverage(float signedDistance, float edge)
        {
            float u = Mathf.Clamp01(0.5f - (signedDistance / (2f * edge)));
            return u * u * (3f - (2f * u));
        }

        /// <summary>One anamorphic flare arm: thin across <paramref name="across"/>, fading along <paramref name="along"/>.</summary>
        private static float Streak(float across, float along, float width)
        {
            float t = across / width;
            float thin = Mathf.Exp(-t * t);
            float fade = Mathf.Max(0f, 1f - Mathf.Abs(along));
            return thin * fade * fade;
        }

        // ------------------------------------------------------------------ deterministic noise

        /// <summary>Three-octave value noise in 0..1. Deterministic — no <c>Random</c>, so textures are reproducible.</summary>
        private static float FractalNoise(float x, float y)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float frequency = 3f;

            for (int octave = 0; octave < 3; octave++)
            {
                sum += ValueNoise(x * frequency, y * frequency) * amplitude;
                frequency *= 2.1f;
                amplitude *= 0.5f;
            }
            return Mathf.Clamp01(sum / 0.875f);
        }

        private static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            // Smoothstep interpolation weights
            float u = xf * xf * (3f - (2f * xf));
            float v = yf * yf * (3f - (2f * yf));

            float a = Hash(xi, yi);
            float b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1);
            float d = Hash(xi + 1, yi + 1);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFF) / (float)0x7FFFFFF;
            }
        }
    }
}
