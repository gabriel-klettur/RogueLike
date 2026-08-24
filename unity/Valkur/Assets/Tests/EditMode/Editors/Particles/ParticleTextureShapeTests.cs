using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Shape = Valkur.Data.ParticleTextureShape;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Guards <see cref="ParticleTextureShape.Leaf"/> and <see cref="ParticleTextureShape.Petal"/>,
    /// the first two silhouettes in the library with a long axis, and — far more importantly —
    /// guards the eight radial shapes they were appended after.
    ///
    /// Three failure modes are locked down here.
    ///
    /// 1. RENUMBERING. The 131 particle preset assets serialize <c>textureShape</c> BY NUMBER.
    ///    Inserting a member, sorting the enum "sensibly", or reusing a retired value repoints
    ///    every preset that used the shifted value at a different texture. It compiles, it passes
    ///    every behavioural test, and it surfaces months later as "the smoke is a ring now". Only
    ///    the numbers can catch that, so the numbers are asserted literally.
    ///
    /// 2. COLLATERAL DAMAGE FROM THE LUMINANCE CHANNEL. Adding Leaf/Petal shading meant RGB
    ///    stopped being unconditionally white. <c>startColor</c> MULTIPLIES the texture, so any
    ///    shape whose luminance drifts below 1 is darkened in every preset that uses it — every
    ///    torch, every explosion, every aura at once, with nothing in any asset changed to explain
    ///    it. Every pre-Leaf shape must still return exactly 1.
    ///
    /// 3. RE-SMOOTHING. Leaf and Petal were rejected once already as smooth, anti-aliased,
    ///    HD-illustration silhouettes pasted onto 16-PPU pixel art. The current design is a
    ///    QUANTISED SPRITE: a handful of logical texel cells (Leaf 5x5, Petal 5x4), sampled at
    ///    cell centres, alpha strictly binary, luminance strictly one of three flat tones from a
    ///    fixed key light. Nothing here is a gradient. The tests below exist to fail loudly the
    ///    moment someone "cleans up" the blockiness back into a curve.
    ///
    /// The generated textures are uploaded with <c>makeNoLongerReadable: true</c>, so
    /// <c>GetPixels</c> is unavailable and the silhouette is asserted through the pure
    /// <see cref="ParticleTextureLibrary.EvaluateAlpha"/> /
    /// <see cref="ParticleTextureLibrary.EvaluateLuminance"/> functions that fill it — they are
    /// the source of truth for the shape, not a convenience. The logical grid itself
    /// (<c>LEAF_COLS</c>/<c>LEAF_ROWS</c>/<c>PETAL_COLS</c>/<c>PETAL_ROWS</c>) and the private
    /// <c>PixelCell</c> cell-centre snap are read by reflection rather than re-implemented, so a
    /// retune of the grid size moves this fixture with it instead of silently going stale.
    ///
    /// Domain Reload is OFF in this project and the library's cache is a static dictionary whose
    /// only reset is <c>[RuntimeInitializeOnLoadMethod]</c>, which never runs in EditMode. The
    /// fixture therefore snapshots the cache in SetUp and destroys exactly the textures its own
    /// tests caused to be generated — no more, so a texture another fixture holds is never pulled
    /// out from under it.
    /// </summary>
    [TestFixture]
    public class ParticleTextureShapeTests
    {
        /// <summary>Every value this enum has ever shipped, name to number. Append-only, forever.</summary>
        private static readonly Dictionary<string, int> SHIPPED_VALUES = new Dictionary<string, int>
        {
            { "Auto", 0 }, { "None", 1 }, { "SoftDot", 2 }, { "Glow", 3 }, { "Spark", 4 },
            { "Smoke", 5 }, { "Ring", 6 }, { "Star", 7 }, { "Leaf", 8 }, { "Petal", 9 },
        };

        /// <summary>The eight shapes that existed before the long-axis pair was appended.</summary>
        private static readonly Shape[] PRE_LEAF_SHAPES =
        {
            Shape.Auto, Shape.None, Shape.SoftDot, Shape.Glow,
            Shape.Spark, Shape.Smoke, Shape.Ring, Shape.Star,
        };

        /// <summary>
        /// The pre-Leaf shapes that both draw something AND are pure functions of the radius.
        /// <see cref="ParticleTextureShape.Smoke"/> is deliberately absent: its MASK is radial, but
        /// it multiplies that mask by a value-noise field sampled at (nx, ny), which is not
        /// symmetric under swapping the two — so Smoke cannot serve as a radial control.
        /// </summary>
        private static readonly Shape[] RADIAL_SHAPES =
        {
            Shape.SoftDot, Shape.Glow, Shape.Spark, Shape.Ring, Shape.Star,
        };

        private static readonly float[] SOFTNESS = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        /// <summary>Finer ladder, for assertions about the DIRECTION softness moves a value in.</summary>
        private static readonly float[] SOFTNESS_LADDER =
        {
            0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f,
        };

        /// <summary>Dense probe grid over the full [-1, 1] domain, reused by every test that has to
        /// sweep "everywhere" rather than one authored coordinate.</summary>
        private static readonly float[] DENSE_COORDS =
        {
            -1f, -0.85f, -0.5f, -0.2f, -0.05f, 0f, 0.05f, 0.2f, 0.5f, 0.85f, 1f,
        };

        private HashSet<int> _preExistingCacheKeys;

        // ── Fixture plumbing ─────────────────────────────────────────────────────

        private static float A(Shape shape, float nx, float ny, float softness) =>
            ParticleTextureLibrary.EvaluateAlpha(shape, nx, ny, softness);

        private static float L(Shape shape, float nx, float ny, float softness) =>
            ParticleTextureLibrary.EvaluateLuminance(shape, nx, ny, softness);

        /// <summary>
        /// Reads a private <c>const int</c> off <see cref="ParticleTextureLibrary"/> by name.
        /// Used instead of hardcoding the grid size, so a retune of <c>LEAF_ROWS</c> etc. moves
        /// this fixture with it — the row-count MISMATCH still fails loudly, but the failure
        /// reads as "update the profile" instead of "the test forgot the grid changed".
        /// </summary>
        private static int Const(string name)
        {
            var field = typeof(ParticleTextureLibrary).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field,
                $"ParticleTextureLibrary.{name} was renamed or removed. This fixture reads the " +
                "pixel-art grid size through it instead of hardcoding 5x5/5x4, precisely so a " +
                "retune doesn't silently desync the test from the shipped shape.");

            return (int)field.GetValue(null);
        }

        /// <summary>(cols, rows) for Leaf or Petal's logical grid, read by reflection.</summary>
        private static (int cols, int rows) GridDims(Shape shape)
        {
            string prefix = shape == Shape.Leaf ? "LEAF" : "PETAL";
            return (Const(prefix + "_COLS"), Const(prefix + "_ROWS"));
        }

        /// <summary>
        /// Invokes the private <see cref="ParticleTextureLibrary"/> cell-centre snap by
        /// reflection rather than re-deriving its formula here — a formula copied into a test can
        /// drift from the one that actually ships and start asserting a lie about where a cell's
        /// centre lands.
        /// </summary>
        private static void PixelCellReflect(float nx, float ny, int cols, int rows, out float cx, out float cy)
        {
            var method = typeof(ParticleTextureLibrary).GetMethod(
                "PixelCell", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method,
                "ParticleTextureLibrary.PixelCell was renamed or removed. Every quantisation " +
                "assertion in this fixture depends on snapping to the SAME cell centre production " +
                "code uses.");

            object[] args = { nx, ny, cols, rows, 0f, 0f };
            method.Invoke(null, args);
            cx = (float)args[4];
            cy = (float)args[5];
        }

        /// <summary>
        /// Occupancy (alpha > 0) for every logical cell of a shape's grid, indexed [iy, ix] with
        /// iy = 0 at the BOTTOM (ny = -1) climbing to iy = rows - 1 at the top.
        /// </summary>
        private static bool[,] Occupancy(Shape shape, int cols, int rows)
        {
            var occ = new bool[rows, cols];
            for (int iy = 0; iy < rows; iy++)
            {
                float nyProbe = (((iy + 0.5f) / rows) * 2f) - 1f;
                for (int ix = 0; ix < cols; ix++)
                {
                    float nxProbe = (((ix + 0.5f) / cols) * 2f) - 1f;
                    PixelCellReflect(nxProbe, nyProbe, cols, rows, out float cx, out float cy);
                    occ[iy, ix] = A(shape, cx, cy, 0f) >= 0.5f;
                }
            }
            return occ;
        }

        private static Dictionary<int, Texture2D> LibraryCache()
        {
            var field = typeof(ParticleTextureLibrary).GetField(
                "_cache", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field,
                "ParticleTextureLibrary._cache was renamed. This fixture cleans up the textures it " +
                "generates through that field; without it every Get() in here leaks a 128x128 RGBA " +
                "texture into an editor session where Domain Reload never clears it.");

            return (Dictionary<int, Texture2D>)field.GetValue(null);
        }

        [SetUp]
        public void SetUp()
        {
            _preExistingCacheKeys = new HashSet<int>(LibraryCache().Keys);
        }

        [TearDown]
        public void TearDown()
        {
            var cache = LibraryCache();

            var mine = new List<int>();
            foreach (int key in cache.Keys)
            {
                if (!_preExistingCacheKeys.Contains(key)) mine.Add(key);
            }

            foreach (int key in mine)
            {
                var tex = cache[key];
                cache.Remove(key);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }

            _preExistingCacheKeys = null;
        }

        // ── 1. The numbers ───────────────────────────────────────────────────────

        /// <summary>
        /// FAILS WHEN: someone reorders, sorts or inserts into ParticleTextureShape.
        /// WHAT BREAKS: all 131 preset assets store this enum as an INTEGER, so a shifted value
        /// repoints presets at a texture nobody chose — torch smoke rendering as a ring, a portal
        /// as a spark — with no diff anywhere to explain it. These eight numbers are shipped data,
        /// not an implementation detail.
        /// </summary>
        [Test]
        public void Enum_TheEightShapesThatPredateLeaf_KeepTheNumbersTheyShippedWith_OrEveryPresetRepointsAtAnotherTexture()
        {
            Assert.AreEqual(0, (int)Shape.Auto, "Auto is the serialized default of every preset that never chose a shape.");
            Assert.AreEqual(1, (int)Shape.None, "None is the legacy untextured quad.");
            Assert.AreEqual(2, (int)Shape.SoftDot);
            Assert.AreEqual(3, (int)Shape.Glow);
            Assert.AreEqual(4, (int)Shape.Spark);
            Assert.AreEqual(5, (int)Shape.Smoke);
            Assert.AreEqual(6, (int)Shape.Ring);
            Assert.AreEqual(7, (int)Shape.Star);
        }

        /// <summary>
        /// FAILS WHEN: Leaf or Petal is renumbered.
        /// WHAT BREAKS: the nine re-authored Plants presets name these two by number in their
        /// .asset files. Move them and the falling leaves come back as whatever shape now owns 8 —
        /// and because Auto is 0, an off-by-one here is the one mistake that would repaint every
        /// preset in the catalogue at once.
        /// </summary>
        [Test]
        public void Enum_LeafIsEightAndPetalIsNine_TheNumbersThePlantsPresetsAlreadyHaveOnDisk()
        {
            Assert.AreEqual(8, (int)Shape.Leaf);
            Assert.AreEqual(9, (int)Shape.Petal);
        }

        /// <summary>
        /// FAILS WHEN: a new shape is INSERTED among the shipped ten instead of appended after
        /// them, or two members are given the same number.
        /// WHAT BREAKS: the same silent repointing as above, arriving through the one edit that
        /// looks harmless — adding a shape next to the one it resembles. A duplicate value is
        /// worse still: two names, one texture, and a cache key that cannot tell them apart.
        /// </summary>
        [Test]
        public void Enum_AnyShapeAddedAfterPetal_IsAppendedAboveNine_NeverInsertedAmongTheShippedTen()
        {
            var seen = new Dictionary<int, string>();

            foreach (string name in Enum.GetNames(typeof(Shape)))
            {
                int value = (int)Enum.Parse(typeof(Shape), name);

                Assert.IsFalse(seen.ContainsKey(value),
                    $"ParticleTextureShape.{name} reuses value {value}, already held by " +
                    $"{(seen.ContainsKey(value) ? seen[value] : "?")}. Presets serialize the number, so " +
                    "two names on one number is two presets that can never be told apart again.");
                seen[value] = name;

                if (SHIPPED_VALUES.TryGetValue(name, out int shipped))
                {
                    Assert.AreEqual(shipped, value,
                        $"ParticleTextureShape.{name} shipped as {shipped} and is now {value}. Every " +
                        "preset asset holding the old number now points somewhere else.");
                    continue;
                }

                Assert.GreaterOrEqual(value, 10,
                    $"ParticleTextureShape.{name} = {value} was inserted among the shipped ten instead " +
                    "of appended. New shapes take the next free number; that is why Auto keeps 0 rather " +
                    "than being sorted anywhere more sensible.");
            }
        }

        // ── 2. The luminance channel did not touch the old shapes ────────────────

        /// <summary>
        /// FAILS WHEN: any shape that predates Leaf starts returning a luminance below 1.
        /// WHAT BREAKS: RGB is MULTIPLIED by the preset's own start colour, so luminance below 1 is
        /// a tint applied to every preset using that shape — every torch, every explosion, every
        /// aura, dimmed or discoloured at once, with nothing in any preset asset changed to explain
        /// it. Before Leaf/Petal shading existed this channel was unconditionally white; the eight
        /// old shapes have to stay that way byte for byte.
        /// </summary>
        [Test]
        public void EvaluateLuminance_EveryShapeThatPredatesLeaf_IsStillExactlyWhite_OrTheShadingTintedTheWholeCatalogue()
        {
            foreach (Shape shape in PRE_LEAF_SHAPES)
            {
                foreach (float softness in SOFTNESS)
                {
                    foreach (float ny in DENSE_COORDS)
                    {
                        foreach (float nx in DENSE_COORDS)
                        {
                            Assert.AreEqual(1f, L(shape, nx, ny, softness), 0f,
                                $"{shape} luminance at ({nx}, {ny}) softness {softness} is no longer pure " +
                                "white. Only Leaf and Petal may spend the RGB channel.");
                        }
                    }
                }
            }
        }

        // ── 3. The radial shapes are the control group — nothing about them moved ─

        /// <summary>
        /// FAILS WHEN: the long-axis machinery leaks into a shape that is supposed to be radial.
        /// WHAT BREAKS: every pre-Leaf shape is a pure function of the radius and must give the
        /// same alpha at (d, 0) as at (0, d); one that starts favouring an axis renders as a
        /// stretched smear on a billboard the game also rotates, which reads as flickering rather
        /// than as a shape. Unlike Leaf/Petal, nothing about the redesign touches these five, so
        /// this control is unchanged from before it landed.
        /// </summary>
        [Test]
        public void EvaluateAlpha_TheRadialShapes_StayPerfectlySymmetricAcrossTheAxes_SoTheLeafPetalRedesignSkewedNothingElse()
        {
            foreach (Shape shape in RADIAL_SHAPES)
            {
                foreach (float softness in SOFTNESS)
                {
                    foreach (float d in new[] { 0.3f, 0.5f, 0.7f })
                    {
                        Assert.AreEqual(A(shape, d, 0f, softness), A(shape, 0f, d, softness), 1e-6f,
                            $"{shape} at distance {d}, softness {softness} is no longer radial.");
                    }
                }
            }
        }

        // ── 4. Quantisation — alpha is binary and flat inside every cell ─────────

        /// <summary>
        /// FAILS WHEN: Leaf or Petal returns a partial alpha value anywhere.
        /// WHAT BREAKS: the whole redesign. The rejected first version was an anti-aliased signed-
        /// distance silhouette; its defining symptom was a ramp of intermediate alpha along every
        /// edge, which is exactly what read as "an HD illustration pasted onto pixel art" instead
        /// of an authored sprite. Coverage on these two shapes must be 0 or 1 and nothing between,
        /// at every coordinate and every softness.
        /// </summary>
        [Test]
        public void EvaluateAlpha_LeafAndPetal_IsStrictlyBinaryEverywhere_OrThisIsAPartiallyTransparentBlobAgain()
        {
            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                foreach (float softness in SOFTNESS)
                {
                    foreach (float ny in DENSE_COORDS)
                    {
                        foreach (float nx in DENSE_COORDS)
                        {
                            float alpha = A(shape, nx, ny, softness);
                            Assert.IsTrue(alpha == 0f || alpha == 1f,
                                $"{shape} at ({nx}, {ny}) softness {softness} returned alpha {alpha}, " +
                                "neither 0 nor 1. An in-between value is an anti-aliased edge — the exact " +
                                "artefact this redesign exists to remove.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// FAILS WHEN: alpha varies between two points that land in the same logical texel cell.
        /// WHAT BREAKS: the quantisation itself. Every sample is snapped to a cell centre before
        /// evaluation precisely so one cell resolves to one value; if two points inside the same
        /// block disagree, the sampler stopped snapping and a soft, position-dependent edge is
        /// back inside the block — the specific bug the cell-centre snap exists to prevent.
        /// </summary>
        [Test]
        public void EvaluateAlpha_LeafAndPetal_IsConstantAcrossEveryPointInsideALogicalCell_OrTheQuantisationGridIsDecorativeOnly()
        {
            float[] insetFractions = { 0.05f, 0.25f, 0.5f, 0.75f, 0.95f };

            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                var (cols, rows) = GridDims(shape);

                for (int iy = 0; iy < rows; iy++)
                {
                    float loY = (((float)iy / rows) * 2f) - 1f;
                    float hiY = ((((float)iy + 1f) / rows) * 2f) - 1f;

                    for (int ix = 0; ix < cols; ix++)
                    {
                        float loX = (((float)ix / cols) * 2f) - 1f;
                        float hiX = ((((float)ix + 1f) / cols) * 2f) - 1f;

                        float? first = null;
                        foreach (float fy in insetFractions)
                        {
                            float ny = loY + (fy * (hiY - loY));
                            foreach (float fx in insetFractions)
                            {
                                float nx = loX + (fx * (hiX - loX));
                                float alpha = A(shape, nx, ny, 0f);

                                if (first == null) { first = alpha; continue; }
                                Assert.AreEqual(first.Value, alpha, 0f,
                                    $"{shape} cell (ix={ix}, iy={iy}): alpha at ({nx:0.###}, {ny:0.###}) " +
                                    $"is {alpha}, disagreeing with {first.Value} sampled earlier in the " +
                                    "SAME cell. A quantised sprite is flat inside every block.");
                            }
                        }
                    }
                }
            }
        }

        // ── 5. The cell profile IS the silhouette ─────────────────────────────────

        /// <summary>
        /// FAILS WHEN: the occupied-cell count per row, bottom to top, no longer matches the
        /// authored profile — Leaf 3,5,5,3,1, Petal 3,5,5,3.
        /// WHAT BREAKS: this sequence is not incidental, it IS the silhouette. Grid dimensions are
        /// read by reflection so a deliberate retune of the grid size doesn't desync this fixture,
        /// but the row-by-row occupancy is the authored intent and has to be pinned literally —
        /// otherwise a "small tweak" to the taper curve reshapes every falling leaf and petal in
        /// the game with nothing in any preset asset changed to explain it.
        /// </summary>
        [Test]
        public void PixelCell_LeafAndPetalRowOccupancy_MatchesTheAuthoredProfile_OrTheSilhouetteWasReshaped()
        {
            AssertRowProfile(Shape.Leaf, new[] { 3, 5, 5, 3, 1 });
            AssertRowProfile(Shape.Petal, new[] { 3, 5, 5, 3 });
        }

        private static void AssertRowProfile(Shape shape, int[] expectedBottomToTop)
        {
            var (cols, rows) = GridDims(shape);
            Assert.AreEqual(expectedBottomToTop.Length, rows,
                $"{shape}: reflection reports {rows} rows but the expected profile has " +
                $"{expectedBottomToTop.Length} entries. The row count itself changed; update the " +
                "profile below to match the new grid, don't just widen this check.");

            var occ = Occupancy(shape, cols, rows);
            var actual = new int[rows];
            for (int iy = 0; iy < rows; iy++)
            {
                int count = 0;
                for (int ix = 0; ix < cols; ix++) if (occ[iy, ix]) count++;
                actual[iy] = count;
            }

            CollectionAssert.AreEqual(expectedBottomToTop, actual,
                $"{shape}: occupied-cell count per row, bottom (iy=0) to top, is " +
                $"[{string.Join(",", actual)}] but the authored profile is " +
                $"[{string.Join(",", expectedBottomToTop)}]. This sequence IS the silhouette — a flat " +
                "chip a pixel artist would draw, not a smooth ellipse — so a mismatch is a reshaped " +
                "leaf or petal, not noise.");
        }

        /// <summary>
        /// FAILS WHEN: either shape's bottom row (iy = 0) collapses to a single occupied cell.
        /// WHAT BREAKS: the specific regression already seen once. A single-cell bottom row is what
        /// made an earlier tuning of Leaf grow a stem and read as a tiny tree instead of something
        /// falling, and the mirror mistake turns Petal into a mushroom. Kept separate from the
        /// exact-profile pin above so a legitimate retune of the OTHER rows doesn't have to touch
        /// this test — it only ever blocks the one shape of failure that already happened.
        /// </summary>
        [Test]
        public void PixelCell_NeitherShapeHasASingleCellBottomRow_OrTheLeafGrowsAStemAndThePetalBecomesAMushroom()
        {
            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                var (cols, rows) = GridDims(shape);
                var occ = Occupancy(shape, cols, rows);

                int bottomRowCount = 0;
                for (int ix = 0; ix < cols; ix++) if (occ[0, ix]) bottomRowCount++;

                Assert.Greater(bottomRowCount, 1,
                    $"{shape}'s bottom row (iy=0) has only {bottomRowCount} occupied cell(s). A " +
                    "single-cell bottom row is the exact regression that grew a stem on the leaf and " +
                    "a stalk on the petal — the base has to be at least a small foot wide, never a " +
                    "point.");
            }
        }

        // ── 6. Luminance — three flat tones, snapped to the same cell as alpha ──

        /// <summary>
        /// FAILS WHEN: Leaf or Petal's luminance takes more than three distinct values across the
        /// whole grid, or varies between two points inside the same logical cell.
        /// WHAT BREAKS: the shading model is a fixed upper-left key light snapped to three flat
        /// tones — full lit / mid / shade — because a pixel artist shading a chip this small
        /// reaches for three values, not a gradient. A fourth value or a within-cell drift means a
        /// continuous ramp crept back in, which is the same "rendered object" look the redesign
        /// exists to remove, just moved from the alpha channel to the RGB one.
        /// </summary>
        [Test]
        public void EvaluateLuminance_LeafAndPetal_TakesAtMostThreeFlatTonesAndIsConstantPerCell_OrThisIsAGradientAgain()
        {
            float[] insetFractions = { 0.05f, 0.5f, 0.95f };

            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                var (cols, rows) = GridDims(shape);

                foreach (float softness in SOFTNESS)
                {
                    var distinct = new HashSet<float>();

                    for (int iy = 0; iy < rows; iy++)
                    {
                        float loY = (((float)iy / rows) * 2f) - 1f;
                        float hiY = ((((float)iy + 1f) / rows) * 2f) - 1f;

                        for (int ix = 0; ix < cols; ix++)
                        {
                            float loX = (((float)ix / cols) * 2f) - 1f;
                            float hiX = ((((float)ix + 1f) / cols) * 2f) - 1f;

                            float? first = null;
                            foreach (float fy in insetFractions)
                            {
                                float ny = loY + (fy * (hiY - loY));
                                foreach (float fx in insetFractions)
                                {
                                    float nx = loX + (fx * (hiX - loX));
                                    float lum = L(shape, nx, ny, softness);

                                    if (first == null) { first = lum; distinct.Add(lum); continue; }
                                    Assert.AreEqual(first.Value, lum, 0f,
                                        $"{shape} cell (ix={ix}, iy={iy}) softness {softness}: " +
                                        $"luminance at ({nx:0.###}, {ny:0.###}) is {lum}, disagreeing " +
                                        $"with {first.Value} sampled earlier in the SAME cell.");
                                }
                            }
                        }
                    }

                    Assert.LessOrEqual(distinct.Count, 3,
                        $"{shape} at softness {softness} produced {distinct.Count} distinct luminance " +
                        "values across the grid. The shading model is THREE flat tones from a single " +
                        "fixed key light — a fourth value means a gradient crept back in.");
                }
            }
        }

        // ── 7. Softness — tone contrast only, never alpha ────────────────────────

        /// <summary>
        /// FAILS WHEN: softness changes the alpha of Leaf or Petal at any coordinate.
        /// WHAT BREAKS: softness is repurposed on these two shapes to drive tone CONTRAST, and
        /// explicitly does not blur anything any more. At 24 screen pixels the blade is already
        /// only ~10 px across; a softness slider that still moves alpha is a blur slider again, and
        /// softness 1 would eat the chip's edges the same way the rejected anti-aliased version did.
        /// </summary>
        [Test]
        public void EvaluateAlpha_LeafAndPetal_IgnoresSoftnessEntirely_OrTheSliderIsBlurringThePixelGridAgain()
        {
            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                foreach (float ny in DENSE_COORDS)
                {
                    foreach (float nx in DENSE_COORDS)
                    {
                        float baseline = A(shape, nx, ny, 0f);

                        foreach (float softness in SOFTNESS_LADDER)
                        {
                            float alpha = A(shape, nx, ny, softness);
                            Assert.AreEqual(baseline, alpha, 0f,
                                $"{shape} at ({nx}, {ny}): alpha at softness {softness} is {alpha}, but " +
                                $"softness 0 gave {baseline}. Softness must never move alpha on this " +
                                "shape.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// FAILS WHEN: a cell whose luminance is below 1 does not rise monotonically toward 1 as
        /// softness increases from 0 to 1.
        /// WHAT BREAKS: the one thing left for softness to control on these shapes. 0 must read as
        /// a hard three-value chip and 1 must flatten it toward a single tone; a value that dips or
        /// stays flat as softness rises means the contrast control silently stopped doing anything,
        /// leaving softness with no visible effect on Leaf/Petal at all.
        /// </summary>
        [Test]
        public void EvaluateLuminance_LeafAndPetal_SoftnessFlattensToneContrastMonotonically_OrTheThreeToneChipLosesItsShading()
        {
            foreach (Shape shape in new[] { Shape.Leaf, Shape.Petal })
            {
                var (cols, rows) = GridDims(shape);

                var tonedCells = new List<(float cx, float cy)>();
                for (int iy = 0; iy < rows; iy++)
                {
                    float nyProbe = (((iy + 0.5f) / rows) * 2f) - 1f;
                    for (int ix = 0; ix < cols; ix++)
                    {
                        float nxProbe = (((ix + 0.5f) / cols) * 2f) - 1f;
                        PixelCellReflect(nxProbe, nyProbe, cols, rows, out float cx, out float cy);
                        if (L(shape, cx, cy, 0f) < 1f) tonedCells.Add((cx, cy));
                    }
                }

                Assert.Greater(tonedCells.Count, 0,
                    $"{shape} has no cell darker than 1 at softness 0 — the key light stopped shading " +
                    "anything, so the shape is a flat cut-out with no fold at all.");

                foreach (var (cx, cy) in tonedCells)
                {
                    float previous = -1f;
                    foreach (float softness in SOFTNESS_LADDER)
                    {
                        float lum = L(shape, cx, cy, softness);

                        Assert.LessOrEqual(lum, 1f,
                            $"{shape} at ({cx:0.##}, {cy:0.##}) softness {softness}: luminance {lum} " +
                            "exceeds 1, brighter than the lit face itself.");

                        if (previous >= 0f)
                        {
                            Assert.Greater(lum, previous,
                                $"{shape} at ({cx:0.##}, {cy:0.##}) softness {softness}: tone " +
                                $"{lum:0.000} did not rise above the previous step's {previous:0.000}. " +
                                "Softer must mean flatter, monotonically.");
                        }
                        previous = lum;
                    }
                }
            }
        }

        // ── 8. The sampler — Point + no mips, or the pixel grid ramps under filtering ─

        /// <summary>
        /// FAILS WHEN: Leaf or Petal is uploaded with anything but FilterMode.Point and a single
        /// mip level, or an unrelated radial shape (SoftDot, as a control) loses its Bilinear
        /// filtering and mip chain.
        /// WHAT BREAKS: everything the quantisation buys. Bilinear sampling ramps every hard cell
        /// edge back into a gradient at render time regardless of how binary the source alpha is —
        /// a bilinear pixel sprite is exactly the "HD illustration" look the user rejected, just
        /// moved from the generator into the GPU sampler. A mip chain is just as fatal a different
        /// way: it averages the three flat tones together the moment the particle is smaller than
        /// the base texture, which is most of a falling leaf's on-screen lifetime.
        /// </summary>
        [Test]
        public void Generate_LeafAndPetal_UsePointFilterWithNoMipChain_WhileOtherShapesStayBilinear_OrThisIsABilinearPixelSpriteAgain()
        {
            var leaf = ParticleTextureLibrary.Get(Shape.Leaf, 0.5f);
            var petal = ParticleTextureLibrary.Get(Shape.Petal, 0.5f);
            var dot = ParticleTextureLibrary.Get(Shape.SoftDot, 0.5f);

            Assert.IsTrue(leaf != null && petal != null && dot != null,
                "Get() returned nothing for one of the shapes under test.");

            Assert.AreEqual(FilterMode.Point, leaf.filterMode,
                "Leaf must sample with FilterMode.Point. Bilinear would ramp every hard cell edge " +
                "back into a gradient, the smooth-edged look this redesign replaced.");
            Assert.AreEqual(1, leaf.mipmapCount,
                "Leaf must upload with no mip chain. A mip chain averages the three flat tones " +
                "together the moment the particle shrinks below the base texture size.");

            Assert.AreEqual(FilterMode.Point, petal.filterMode,
                "Petal must sample with FilterMode.Point, same reasoning as Leaf.");
            Assert.AreEqual(1, petal.mipmapCount,
                "Petal must upload with no mip chain, same reasoning as Leaf.");

            Assert.AreEqual(FilterMode.Bilinear, dot.filterMode,
                "SoftDot is a radial falloff, not a pixel-art chip — it must keep Bilinear " +
                "filtering. A regression here means every glow/spark/ring in the game starts " +
                "showing blocky quantisation artefacts that were never part of their design.");
            Assert.Greater(dot.mipmapCount, 1,
                "SoftDot must keep its mip chain; losing it means every distant or shrunk radial " +
                "particle aliases.");
        }

        // ── 9. The cache — one texture per (shape, softness step) ────────────────

        /// <summary>
        /// FAILS WHEN: the library regenerates a leaf texture per request instead of caching it.
        /// WHAT BREAKS: a 128x128 RGBA texture is built texel by texel on the CPU. A canopy preset
        /// that misses the cache builds that on every emitter spawn — and leaks one texture per
        /// miss, since they are DontSave and nothing else collects them.
        /// </summary>
        [Test]
        public void Get_Leaf_ReturnsOneCachedTexturePerSoftnessStep_OrEveryEmitterRebuildsAndLeaksOne()
        {
            var first = ParticleTextureLibrary.Get(Shape.Leaf, 0.5f);
            var second = ParticleTextureLibrary.Get(Shape.Leaf, 0.5f);

            Assert.IsTrue(first != null,
                "Get(Leaf) returned nothing — an authored leaf preset would fall back to the untextured quad.");
            Assert.IsInstanceOf<Texture2D>(first);
            Assert.AreSame(first, second, "the library must cache, not regenerate.");

            var hard = ParticleTextureLibrary.Get(Shape.Leaf, 0f);
            var soft = ParticleTextureLibrary.Get(Shape.Leaf, 1f);

            Assert.AreNotSame(hard, soft,
                "softness is part of the cache key. Collapse it and every leaf in the game renders at " +
                "whichever softness happened to be requested first.");
            Assert.AreNotSame(first, hard);
            Assert.AreNotSame(first, soft);

            // Nothing here reads the texels back: Generate() uploads with makeNoLongerReadable,
            // so GetPixels is not available and EvaluateAlpha is the only way to see the shape.
        }

        /// <summary>
        /// FAILS WHEN: the (shape, softness step) cache key collides between the two new shapes.
        /// WHAT BREAKS: the key is arithmetic — shape * (steps + 1) + step — so a shape appended
        /// without widening the stride hands one shape's texture to the other. Leaf and Petal are
        /// adjacent numbers and adjacent in the catalogue, so the swap would be silent: petals would
        /// simply start falling as leaves.
        /// </summary>
        [Test]
        public void Get_LeafAndPetal_NeverShareATexture_OrOneShapeIsServingTheOthersSilhouette()
        {
            for (int step = 0; step <= 16; step++)
            {
                float softness = step / 16f;

                var leaf = ParticleTextureLibrary.Get(Shape.Leaf, softness);
                var petal = ParticleTextureLibrary.Get(Shape.Petal, softness);

                Assert.IsTrue(leaf != null && petal != null, $"softness step {step} produced no texture.");
                Assert.AreNotSame(leaf, petal, $"Leaf and Petal collide in the cache at softness step {step}.");
            }
        }

        // ── 10. Auto resolution is unchanged ──────────────────────────────────────

        /// <summary>
        /// FAILS WHEN: Auto-resolution starts swallowing an explicitly authored Leaf or Petal.
        /// WHAT BREAKS: the nine Plants presets opt in by NAMING the shape. If ResolveShape stopped
        /// passing non-Auto values through, an authored leaf would come back as a Glow and the opt-in
        /// would be unreachable from the F1 editor.
        /// </summary>
        [Test]
        public void ResolveShape_AnAuthoredLeafOrPetal_PassesThroughUntouched_OrTheOptInIsUnreachable()
        {
            Assert.AreEqual(Shape.Leaf,
                ParticleTextureLibrary.ResolveShape(Shape.Leaf, "falling_leaf", additive: false));
            Assert.AreEqual(Shape.Leaf,
                ParticleTextureLibrary.ResolveShape(Shape.Leaf, "aura", additive: true));
            Assert.AreEqual(Shape.Petal,
                ParticleTextureLibrary.ResolveShape(Shape.Petal, "falling_leaf", additive: false));
            Assert.AreEqual(Shape.Petal,
                ParticleTextureLibrary.ResolveShape(Shape.Petal, null, additive: true));
        }

        /// <summary>
        /// FAILS WHEN: someone points the falling_leaf kind's Auto case at the new Leaf shape.
        /// WHAT BREAKS: Auto is the SERIALIZED DEFAULT — it is what every preset that never chose a
        /// shape holds. Re-pointing this one case rewrites the silhouette of every such preset at
        /// once: one edit, no diff to read, and vegetation across the whole world changes shape
        /// without a single asset being touched. A leaf preset opts in explicitly instead.
        /// </summary>
        [Test]
        public void ResolveShape_TheFallingLeafKind_StillResolvesToSoftDot_SoAddingLeafRewroteNoExistingPreset()
        {
            foreach (string kind in new[] { "falling_leaf", "water_flow", "water_fountain" })
            {
                Assert.AreEqual(Shape.SoftDot,
                    ParticleTextureLibrary.ResolveShape(Shape.Auto, kind, additive: false),
                    $"kind '{kind}' must still resolve to SoftDot under Auto.");
            }
        }
    }
}
