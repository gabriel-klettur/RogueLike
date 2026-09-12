using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.UI
{
    /// <summary>
    /// The title's letterforms, as STROKES rather than as a texture — so the game's name can be
    /// drawn by a few thousand points of light instead of by a PNG.
    ///
    /// <para><b>Why a stroke table and not a font, a texture or a TMP readback.</b> Three
    /// options were weighed. A TextMeshPro string rendered into a RenderTexture and read back
    /// gives real typography and costs a camera, a render target, a <c>ReadPixels</c> stall at
    /// boot and a point cloud with no notion of direction — the motes could settle into the word
    /// but never flow ALONG it. A bitmap mask authored as an image is the PNG this replaces,
    /// one resolution and one word. A stroke table is pure data: deterministic, testable in
    /// EditMode with no scene, free of assets, and it carries the one thing the other two cannot
    /// — the DIRECTION of the pen, which is what lets a mote travel down a stem and settle at its
    /// end rather than teleport into place.</para>
    ///
    /// <para><b>Coordinates.</b> One em box per glyph: x from 0 to the glyph's own
    /// <see cref="Advance"/>, y from 0 at the baseline to 1 at the cap height. Every glyph is a
    /// capital — a title is set in caps, and a lowercase set would double the table for letters
    /// the title never uses.</para>
    ///
    /// <para><b>The alphabet is complete (A-Z, 0-9, and the punctuation a title needs)</b> even
    /// though the shipped title is six letters. That is what makes the title DATA: renaming the
    /// game, adding a subtitle or localising the word is a string change, not an art change. A
    /// table that only knew V, A, L, K, U and R would be the PNG again, spelled differently.</para>
    ///
    /// <para><b>A missing glyph is skipped, never substituted.</b> Drawing a box or a fallback
    /// letter would put a character in the game's name that nobody authored; advancing past it
    /// leaves a gap that is obviously wrong and obviously the author's to fix.</para>
    /// </summary>
    public static class TitleGlyphStrokes
    {
        /// <summary>Advance of a space, in em units.</summary>
        public const float SpaceAdvance = 0.34f;

        /// <summary>Gap left between two glyphs, in em units, before per-pair tracking.</summary>
        public const float DefaultTracking = 0.12f;

        /// <summary>
        /// How far a POINTED or ROUND letter is drawn past the line it sits on, in em units.
        ///
        /// <para>Declared rather than buried in the glyph table because it is the answer to a
        /// question a reader will otherwise ask of every stray coordinate: why does the A reach
        /// 1.025 when the cap line is 1? Because a point and a flat top that both stop at the
        /// line do not look the same height — the point carries almost no ink up there, so it
        /// reads short beside a flat stem. Every typeface answers this the same way. It is also
        /// what lets <c>EveryPoint_SitsInsideItsOwnEmBox</c> keep being a real guard: the bound
        /// is the box PLUS a declared overshoot, so a glyph that wanders still fails.</para>
        /// </summary>
        public const float Overshoot = 0.025f;

        /// <summary>One sampled point of the title: where it sits and which way the pen was going.</summary>
        public readonly struct TitlePoint
        {
            /// <summary>Position in the laid-out title's own space (x right, y up, baseline at 0).</summary>
            public readonly Vector2 Position;

            /// <summary>Unit direction of the stroke here. What a flowing assembly follows.</summary>
            public readonly Vector2 Tangent;

            /// <summary>0 at the first glyph's left edge, 1 at the last glyph's right edge.</summary>
            public readonly float Across;

            /// <summary>Index of the glyph this point belongs to, so a per-letter beat is possible.</summary>
            public readonly int GlyphIndex;

            /// <summary>
            /// 0 at the middle of the stroke, 1 at its edge. This is what lets the word be drawn
            /// like hot metal — a near-white core inside a warm rim — rather than as one flat
            /// colour, and it is the only quantity here that no mask or texture readback could
            /// have given: it is a property of the PEN, not of the pixels.
            /// </summary>
            public readonly float Depth;

            public TitlePoint(Vector2 position, Vector2 tangent, float across, int glyphIndex, float depth)
            {
                Position = position;
                Tangent = tangent;
                Across = across;
                GlyphIndex = glyphIndex;
                Depth = depth;
            }
        }

        // ── The table ────────────────────────────────────────────────────────
        //
        // Each glyph is a list of POLYLINES. A closed shape repeats its first point at the end.
        // Values are read off a 50-unit grid and written as hundredths, which is why they look
        // arbitrary: 0.34 is 17/50, not a tuned float.

        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly Dictionary<char, float> Advances = new Dictionary<char, float>
        {
            ['A'] = 0.68f, ['B'] = 0.62f, ['C'] = 0.64f, ['D'] = 0.64f, ['E'] = 0.56f,
            ['F'] = 0.54f, ['G'] = 0.68f, ['H'] = 0.66f, ['I'] = 0.28f, ['J'] = 0.52f,
            ['K'] = 0.64f, ['L'] = 0.54f, ['M'] = 0.84f, ['N'] = 0.68f, ['O'] = 0.70f,
            ['P'] = 0.60f, ['Q'] = 0.70f, ['R'] = 0.62f, ['S'] = 0.60f, ['T'] = 0.62f,
            ['U'] = 0.66f, ['V'] = 0.68f, ['W'] = 0.94f, ['X'] = 0.66f, ['Y'] = 0.66f,
            ['Z'] = 0.60f,
            ['0'] = 0.62f, ['1'] = 0.50f, ['2'] = 0.60f, ['3'] = 0.60f, ['4'] = 0.62f,
            ['5'] = 0.60f, ['6'] = 0.60f, ['7'] = 0.58f, ['8'] = 0.62f, ['9'] = 0.60f,
            ['.'] = 0.24f, [','] = 0.24f, [':'] = 0.24f, ['-'] = 0.40f, ['\''] = 0.20f,
            ['!'] = 0.26f, ['?'] = 0.54f, [' '] = SpaceAdvance,
        };

        private static Dictionary<char, Vector2[][]> s_glyphs;

        private static Dictionary<char, Vector2[][]> Glyphs => s_glyphs ?? (s_glyphs = Build());

        /// <summary>
        /// Static mutable state with Domain Reload off. A direct assignment rather than a helper
        /// call, because <c>DomainReloadStaticResetTests</c> reads this method's raw IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_glyphs = null;
        }

        private static Vector2[][] G(params Vector2[][] strokes) => strokes;
        private static Vector2[] S(params Vector2[] pts) => pts;
        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Dictionary<char, Vector2[][]> Build()
        {
            var g = new Dictionary<char, Vector2[][]>();

            // OVERSHOOT on the apex, and it is a measurement about the eye rather than a taste.
            // A point and a flat top that both stop at the cap line do not look the same height:
            // the point has almost no ink up there, so it reads short beside the flat stem of an
            // L or a K. Every typeface answers this the same way — pointed and round letters are
            // drawn a little past the line. 2.5 % here, which is the usual order.
            g['A'] = G(S(V(0f, 0f), V(0.34f, 1.025f)),
                       S(V(0.34f, 1.025f), V(0.68f, 0f)),
                       // The crossbar sits a little lower than the middle: a bar at half height
                       // makes the counter above it look pinched, and the letter reads top-heavy.
                       S(V(0.125f, 0.35f), V(0.555f, 0.35f)));

            g['B'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0f, 1f), V(0.42f, 1f), V(0.58f, 0.87f), V(0.58f, 0.67f), V(0.42f, 0.54f), V(0f, 0.54f)),
                       S(V(0f, 0.54f), V(0.48f, 0.54f), V(0.62f, 0.41f), V(0.62f, 0.14f), V(0.48f, 0f), V(0f, 0f)));

            g['C'] = G(S(V(0.62f, 0.80f), V(0.50f, 0.96f), V(0.28f, 1f), V(0.10f, 0.85f),
                         V(0.02f, 0.60f), V(0.02f, 0.40f), V(0.10f, 0.15f), V(0.28f, 0f),
                         V(0.50f, 0.04f), V(0.62f, 0.20f)));

            g['D'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0f, 1f), V(0.34f, 1f), V(0.58f, 0.80f), V(0.62f, 0.50f),
                         V(0.58f, 0.20f), V(0.34f, 0f), V(0f, 0f)));

            g['E'] = G(S(V(0.54f, 1f), V(0f, 1f), V(0f, 0f), V(0.54f, 0f)),
                       S(V(0f, 0.52f), V(0.44f, 0.52f)));

            g['F'] = G(S(V(0.52f, 1f), V(0f, 1f), V(0f, 0f)),
                       S(V(0f, 0.55f), V(0.42f, 0.55f)));

            g['G'] = G(S(V(0.64f, 0.80f), V(0.50f, 0.96f), V(0.28f, 1f), V(0.10f, 0.85f),
                         V(0.02f, 0.60f), V(0.02f, 0.40f), V(0.10f, 0.15f), V(0.30f, 0f),
                         V(0.52f, 0.05f), V(0.64f, 0.22f), V(0.64f, 0.44f), V(0.38f, 0.44f)));

            g['H'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0.66f, 0f), V(0.66f, 1f)),
                       S(V(0f, 0.52f), V(0.66f, 0.52f)));

            g['I'] = G(S(V(0.14f, 0f), V(0.14f, 1f)),
                       S(V(0f, 1f), V(0.28f, 1f)),
                       S(V(0f, 0f), V(0.28f, 0f)));

            g['J'] = G(S(V(0.40f, 1f), V(0.40f, 0.22f), V(0.30f, 0.04f), V(0.13f, 0f), V(0f, 0.14f)),
                       S(V(0.16f, 1f), V(0.52f, 1f)));

            // THE JUNCTION IS THE WHOLE LETTER. The shipped K had its arm land on the stem at
            // y = 0.46 while its leg started, unattached, at (0.20, 0.58) — so the two limbs met
            // the stem at different places and the letter read as three separate strokes that
            // happen to be near each other. The leg now springs from a point that lies ON the
            // arm: the arm runs (0.62, 1) to (0.04, 0.48), and at y = 0.55 that line is at
            // x = 0.118, which is where the leg starts. One junction, and the K stops looking
            // broken at any size.
            g['K'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0.62f, 1f), V(0.04f, 0.48f)),
                       S(V(0.118f, 0.55f), V(0.64f, 0f)));

            g['L'] = G(S(V(0f, 1f), V(0f, 0f), V(0.54f, 0f)));

            g['M'] = G(S(V(0f, 0f), V(0f, 1f), V(0.42f, 0.34f), V(0.84f, 1f), V(0.84f, 0f)));

            g['N'] = G(S(V(0f, 0f), V(0f, 1f), V(0.68f, 0f), V(0.68f, 1f)));

            g['O'] = G(S(V(0.35f, 1f), V(0.58f, 0.90f), V(0.70f, 0.62f), V(0.70f, 0.38f),
                         V(0.58f, 0.10f), V(0.35f, 0f), V(0.12f, 0.10f), V(0f, 0.38f),
                         V(0f, 0.62f), V(0.12f, 0.90f), V(0.35f, 1f)));

            g['P'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0f, 1f), V(0.42f, 1f), V(0.60f, 0.86f), V(0.60f, 0.64f), V(0.42f, 0.50f), V(0f, 0.50f)));

            g['Q'] = G(S(V(0.35f, 1f), V(0.58f, 0.90f), V(0.70f, 0.62f), V(0.70f, 0.38f),
                         V(0.58f, 0.10f), V(0.35f, 0f), V(0.12f, 0.10f), V(0f, 0.38f),
                         V(0f, 0.62f), V(0.12f, 0.90f), V(0.35f, 1f)),
                       S(V(0.44f, 0.26f), V(0.72f, -0.06f)));

            // Same argument as the K: the leg starts where the BOWL closes rather than in the
            // middle of the white space under it, so it reads as growing out of the letter
            // instead of leaning against it. It also finishes wider than the bowl, which is what
            // keeps an R from looking like a P with a mistake.
            g['R'] = G(S(V(0f, 0f), V(0f, 1f)),
                       S(V(0f, 1f), V(0.42f, 1f), V(0.60f, 0.86f), V(0.60f, 0.64f), V(0.42f, 0.50f), V(0f, 0.50f)),
                       S(V(0.34f, 0.50f), V(0.66f, 0f)));

            g['S'] = G(S(V(0.58f, 0.85f), V(0.44f, 0.99f), V(0.20f, 1f), V(0.06f, 0.87f),
                         V(0.06f, 0.68f), V(0.20f, 0.56f), V(0.42f, 0.49f), V(0.56f, 0.37f),
                         V(0.56f, 0.15f), V(0.42f, 0.02f), V(0.16f, 0.01f), V(0.02f, 0.15f)));

            g['T'] = G(S(V(0f, 1f), V(0.62f, 1f)),
                       S(V(0.31f, 1f), V(0.31f, 0f)));

            // A curve needs overshoot for the same reason a point does: the bottom of a U touches
            // the baseline at a tangent, so it reads high against a flat foot unless it dips.
            g['U'] = G(S(V(0f, 1f), V(0f, 0.26f), V(0.12f, 0.04f), V(0.33f, -0.018f),
                         V(0.54f, 0.04f), V(0.66f, 0.26f), V(0.66f, 1f)));

            // The same overshoot, downward: a V's vertex is a point sitting on the baseline and
            // needs to drop below it to look level with the flat feet of the letters beside it.
            g['V'] = G(S(V(0f, 1f), V(0.34f, -0.025f), V(0.68f, 1f)));

            g['W'] = G(S(V(0f, 1f), V(0.18f, 0f), V(0.47f, 0.66f), V(0.76f, 0f), V(0.94f, 1f)));

            g['X'] = G(S(V(0f, 1f), V(0.66f, 0f)),
                       S(V(0f, 0f), V(0.66f, 1f)));

            g['Y'] = G(S(V(0f, 1f), V(0.33f, 0.48f), V(0.66f, 1f)),
                       S(V(0.33f, 0.48f), V(0.33f, 0f)));

            g['Z'] = G(S(V(0f, 1f), V(0.60f, 1f), V(0f, 0f), V(0.60f, 0f)));

            g['0'] = G(S(V(0.31f, 1f), V(0.52f, 0.90f), V(0.62f, 0.62f), V(0.62f, 0.38f),
                         V(0.52f, 0.10f), V(0.31f, 0f), V(0.10f, 0.10f), V(0f, 0.38f),
                         V(0f, 0.62f), V(0.10f, 0.90f), V(0.31f, 1f)),
                       S(V(0.10f, 0.18f), V(0.52f, 0.82f)));

            g['1'] = G(S(V(0.06f, 0.80f), V(0.30f, 1f), V(0.30f, 0f)),
                       S(V(0.06f, 0f), V(0.50f, 0f)));

            g['2'] = G(S(V(0.04f, 0.82f), V(0.18f, 0.98f), V(0.40f, 1f), V(0.54f, 0.86f),
                         V(0.52f, 0.62f), V(0.02f, 0f), V(0.58f, 0f)));

            g['3'] = G(S(V(0.04f, 0.86f), V(0.20f, 1f), V(0.44f, 0.98f), V(0.56f, 0.84f),
                         V(0.48f, 0.60f), V(0.26f, 0.53f)),
                       S(V(0.26f, 0.53f), V(0.50f, 0.45f), V(0.58f, 0.26f), V(0.46f, 0.05f),
                         V(0.20f, 0.01f), V(0.04f, 0.14f)));

            g['4'] = G(S(V(0.44f, 0f), V(0.44f, 1f), V(0.02f, 0.30f), V(0.62f, 0.30f)));

            g['5'] = G(S(V(0.54f, 1f), V(0.10f, 1f), V(0.06f, 0.58f), V(0.24f, 0.66f),
                         V(0.46f, 0.60f), V(0.58f, 0.42f), V(0.52f, 0.16f), V(0.30f, 0.01f),
                         V(0.06f, 0.10f)));

            g['6'] = G(S(V(0.54f, 0.92f), V(0.32f, 1f), V(0.12f, 0.84f), V(0.04f, 0.50f),
                         V(0.06f, 0.24f), V(0.22f, 0.02f), V(0.44f, 0.04f), V(0.58f, 0.22f),
                         V(0.54f, 0.44f), V(0.34f, 0.54f), V(0.12f, 0.46f)));

            g['7'] = G(S(V(0f, 1f), V(0.58f, 1f), V(0.20f, 0f)),
                       S(V(0.14f, 0.48f), V(0.44f, 0.48f)));

            g['8'] = G(S(V(0.31f, 0.54f), V(0.10f, 0.64f), V(0.06f, 0.84f), V(0.20f, 0.99f),
                         V(0.42f, 0.99f), V(0.56f, 0.84f), V(0.52f, 0.64f), V(0.31f, 0.54f)),
                       S(V(0.31f, 0.54f), V(0.08f, 0.42f), V(0.02f, 0.20f), V(0.18f, 0.01f),
                         V(0.44f, 0.01f), V(0.60f, 0.20f), V(0.54f, 0.42f), V(0.31f, 0.54f)));

            g['9'] = G(S(V(0.06f, 0.08f), V(0.28f, 0f), V(0.48f, 0.16f), V(0.56f, 0.50f),
                         V(0.54f, 0.76f), V(0.38f, 0.98f), V(0.16f, 0.96f), V(0.02f, 0.78f),
                         V(0.06f, 0.56f), V(0.26f, 0.46f), V(0.48f, 0.54f)));

            g['.'] = G(S(V(0.08f, 0.02f), V(0.16f, 0.02f), V(0.16f, 0.10f), V(0.08f, 0.10f), V(0.08f, 0.02f)));
            g[','] = G(S(V(0.16f, 0.10f), V(0.14f, 0.02f), V(0.06f, -0.08f)));
            g[':'] = G(S(V(0.08f, 0.02f), V(0.16f, 0.02f), V(0.16f, 0.10f), V(0.08f, 0.10f), V(0.08f, 0.02f)),
                       S(V(0.08f, 0.50f), V(0.16f, 0.50f), V(0.16f, 0.58f), V(0.08f, 0.58f), V(0.08f, 0.50f)));
            g['-'] = G(S(V(0.04f, 0.48f), V(0.36f, 0.48f)));
            g['\''] = G(S(V(0.10f, 1f), V(0.10f, 0.76f)));
            g['!'] = G(S(V(0.13f, 1f), V(0.13f, 0.26f)),
                       S(V(0.09f, 0.02f), V(0.17f, 0.02f), V(0.17f, 0.10f), V(0.09f, 0.10f), V(0.09f, 0.02f)));
            g['?'] = G(S(V(0.04f, 0.84f), V(0.18f, 0.99f), V(0.38f, 0.98f), V(0.50f, 0.84f),
                         V(0.46f, 0.64f), V(0.27f, 0.52f), V(0.27f, 0.30f)),
                       S(V(0.23f, 0.02f), V(0.31f, 0.02f), V(0.31f, 0.10f), V(0.23f, 0.10f), V(0.23f, 0.02f)));

            return g;
        }

        // ── Queries ──────────────────────────────────────────────────────────

        /// <summary>How wide <paramref name="c"/> is, in em units. 0 for a glyph with no entry.</summary>
        public static float Advance(char c)
        {
            c = char.ToUpperInvariant(c);
            return Advances.TryGetValue(c, out float a) ? a : 0f;
        }

        /// <summary>True when the table can draw this character (a space counts).</summary>
        public static bool Has(char c)
        {
            c = char.ToUpperInvariant(c);
            return c == ' ' || Glyphs.ContainsKey(c);
        }

        /// <summary>The polylines of one glyph, or null. Exposed so a test can measure them.</summary>
        public static Vector2[][] Strokes(char c)
        {
            c = char.ToUpperInvariant(c);
            return Glyphs.TryGetValue(c, out var s) ? s : null;
        }

        /// <summary>
        /// Total width of <paramref name="text"/> in em units, including tracking between
        /// glyphs but never after the last one.
        /// </summary>
        public static float MeasureEm(string text, float tracking = DefaultTracking)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float w = 0f;
            int drawn = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (!Has(c)) continue;
                if (drawn > 0) w += tracking;
                w += Advance(c);
                drawn++;
            }
            return w;
        }

        // ── Sampling ─────────────────────────────────────────────────────────

        /// <summary>
        /// Lays out <paramref name="text"/> and walks every stroke, dropping points along the
        /// path and spreading them across the stroke's WIDTH so the letters read as carved bars
        /// rather than as hairlines.
        ///
        /// <para>Pure and deterministic: the same arguments give the same cloud, which is what
        /// lets an EditMode test assert on the shape and what keeps the title identical between
        /// two runs. The jitter comes from a local hash of the sample index, never from
        /// <c>UnityEngine.Random</c>, whose sequence a spell cast elsewhere would move.</para>
        /// </summary>
        /// <param name="text">What to draw. Characters with no glyph are skipped.</param>
        /// <param name="capHeight">Height of a capital in the caller's units (pixels).</param>
        /// <param name="strokeWidth">How thick a stroke is, in the same units.</param>
        /// <param name="spacing">Distance between two points along the path.</param>
        /// <param name="rowsAcross">How many points sit side by side across the stroke.</param>
        /// <param name="tracking">Gap between glyphs, in em units.</param>
        /// <param name="seed">Chooses the jitter. Same seed, same cloud.</param>
        /// <param name="weightContrast">
        /// How much thinner a HORIZONTAL stroke is than a vertical one, 0..1. This is the single
        /// thing that separates a lettering from a wireframe: every typeface with a personality
        /// has stress, and the shipped title had none — all 45 glyphs drew every stroke at one
        /// width, which is what made a hand-built alphabet read as a default one. It is derived
        /// from the stroke's own DIRECTION rather than authored per glyph, so one number gives
        /// the whole alphabet its stress and no letter can be forgotten.
        /// </param>
        /// <param name="terminalFlare">
        /// How much a stroke SWELLS at its two ends, as a fraction of its width. A cut that is
        /// wider where the chisel entered and left is what makes a letter read as carved rather
        /// than as drawn, and it is the cheapest character an alphabet like this can carry.
        /// </param>
        public static List<TitlePoint> Sample(string text, float capHeight, float strokeWidth,
                                              float spacing, int rowsAcross,
                                              float tracking = DefaultTracking, int seed = 8117,
                                              float weightContrast = 0f, float terminalFlare = 0f)
        {
            var points = new List<TitlePoint>(1024);
            if (string.IsNullOrEmpty(text) || capHeight <= 0f || spacing <= 0f) return points;
            rowsAcross = Mathf.Max(1, rowsAcross);
            weightContrast = Mathf.Clamp01(weightContrast);
            terminalFlare = Mathf.Max(0f, terminalFlare);

            float totalEm = MeasureEm(text, tracking);
            float totalWidth = Mathf.Max(0.0001f, totalEm * capHeight);

            float penEm = 0f;
            int drawn = 0;
            int glyphIndex = 0;
            uint hash = (uint)seed * 2654435761u + 1u;

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (!Has(c)) continue;
                if (drawn > 0) penEm += tracking;

                var strokes = Strokes(c);
                if (strokes != null)
                {
                    float originX = penEm * capHeight;
                    for (int s = 0; s < strokes.Length; s++)
                        WalkStroke(strokes[s], originX, capHeight, strokeWidth, spacing, rowsAcross,
                                   totalWidth, glyphIndex, ref hash, points,
                                   weightContrast, terminalFlare);
                }

                penEm += Advance(c);
                drawn++;
                glyphIndex++;
            }

            return points;
        }

        /// <summary>The width a HORIZONTAL stroke keeps at full contrast, against a vertical one.</summary>
        private const float ThinStrokeFactor = 0.52f;

        private static void WalkStroke(Vector2[] stroke, float originX, float capHeight,
                                       float strokeWidth, float spacing, int rowsAcross,
                                       float totalWidth, int glyphIndex, ref uint hash,
                                       List<TitlePoint> into,
                                       float weightContrast = 0f, float terminalFlare = 0f)
        {
            if (stroke == null || stroke.Length < 2) return;

            // The stroke's own length, walked once up front: the flare is a function of how far
            // along the WHOLE stroke a point sits, and a per-segment u would put a swelling at
            // every corner of a curve rather than at its two ends.
            float strokeLength = 0f;
            for (int k = 0; k < stroke.Length - 1; k++)
                strokeLength += ((stroke[k + 1] - stroke[k]) * capHeight).magnitude;
            strokeLength = Mathf.Max(0.0001f, strokeLength);
            float walked = 0f;

            for (int seg = 0; seg < stroke.Length - 1; seg++)
            {
                Vector2 a = new Vector2(originX + stroke[seg].x * capHeight, stroke[seg].y * capHeight);
                Vector2 b = new Vector2(originX + stroke[seg + 1].x * capHeight, stroke[seg + 1].y * capHeight);
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len <= 0.0001f) continue;
                Vector2 tangent = d / len;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);

                // The joint between two segments is sampled once, not twice: the last point of a
                // segment is dropped unless it is the stroke's own end. Sampling both sides of
                // every corner doubles the density exactly where a letter already has the most
                // ink, which reads as a bright blob at every elbow.
                bool lastSegment = seg == stroke.Length - 2;
                int steps = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
                int limit = lastSegment ? steps : steps - 1;

                // STRESS: a vertical stroke keeps its full width, a horizontal one is cut to
                // ThinStrokeFactor, and a diagonal lands between them. This is the classic
                // vertical-axis stress of a humanist letter, and it is what makes an A read as a
                // letterform rather than as three bars of pipe. Derived from the direction, so the
                // whole alphabet gets it from one dial and no glyph can be left out.
                float stress = Mathf.Lerp(1f, Mathf.Lerp(ThinStrokeFactor, 1f, Mathf.Abs(tangent.y)),
                                          weightContrast);

                for (int step = 0; step <= limit; step++)
                {
                    float t = steps == 0 ? 0f : step / (float)steps;
                    Vector2 basePos = Vector2.Lerp(a, b, t);

                    // FLARE: how far along the whole stroke this point sits, and a swelling that
                    // is strongest at both ends and gone in the middle. Cubed rather than linear
                    // so the middle of a stem stays the width it was authored at and only the
                    // last fifth opens out — a linear ramp makes every stroke a lens.
                    float u = (walked + len * t) / strokeLength;
                    float flare = 1f + terminalFlare * Mathf.Pow(Mathf.Abs(u * 2f - 1f), 3f);
                    float localWidth = strokeWidth * stress * flare;

                    // Rows follow the LOCAL width so the ink density stays even: holding the row
                    // count fixed would pack a thin crossbar as tightly as a full stem and the
                    // contrast would read as a brightness difference instead of a weight one.
                    int rowsHere = Mathf.Max(1, Mathf.RoundToInt(rowsAcross * localWidth
                                                                 / Mathf.Max(0.0001f, strokeWidth)));

                    // The jitter is half a ROW SPACING, and the rows are therefore laid inside a
                    // band narrowed by exactly that much — so a jittered point lands on the bar's
                    // edge and never past it. Spreading the rows over the full width and THEN
                    // jittering them (which is what this did first) makes the drawn bar wider than
                    // the one that was authored, by an amount that depends on the row count:
                    // measured on a stroke of 20, five rows drew 21.6 and TWO rows drew 30.4 —
                    // half again as thick — because at two rows the spacing IS the whole bar.
                    // Nothing failed; `strokeWidth` simply stopped meaning the title's weight,
                    // and the weight then moved whenever the mote budget changed the row count.
                    float jitterAmplitude = 0.45f / Mathf.Max(1, rowsHere - 1);
                    float halfBand = Mathf.Max(0f, 0.5f - jitterAmplitude);

                    for (int row = 0; row < rowsHere; row++)
                    {
                        // Rows are spread evenly across that band and then jittered, so the bar has
                        // a clean edge (a fully random spread frays it into a cloud) while no two
                        // points sit exactly on a line (which reads as a printed rule).
                        float across = rowsHere == 1
                            ? 0f
                            : ((row / (float)(rowsHere - 1)) - 0.5f) * 2f * halfBand;
                        float jitter = (NextUnit(ref hash) - 0.5f) * 2f * jitterAmplitude;
                        float offset = (across + jitter) * localWidth;
                        Vector2 p = basePos + normal * offset;
                        // A hair of jitter ALONG the path as well, or the rows read as a comb.
                        p += tangent * ((NextUnit(ref hash) - 0.5f) * spacing * 0.6f);

                        float acrossTitle = totalWidth <= 0f ? 0f : Mathf.Clamp01(p.x / totalWidth);
                        // |across + jitter| is now bounded by exactly 0.5, so this maps the bar's
                        // centre to 0 and its edge to 1 with nothing saturating at the clamp.
                        float depth = Mathf.Clamp01(Mathf.Abs(across + jitter) * 2f);
                        into.Add(new TitlePoint(p, tangent, acrossTitle, glyphIndex, depth));
                    }
                }

                walked += len;
            }
        }

        /// <summary>xorshift32. A local sequence so nothing else in the game can move it.</summary>
        private static float NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
