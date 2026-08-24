using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Cat = Valkur.Gameplay.VFX.ParticlePresetCategory.Category;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Data invariants for the Plants tab of the Particles Editor (F1) — every preset that
    /// <see cref="ParticlePresetCategory"/> files under
    /// <see cref="ParticlePresetCategory.Category.Vegetation"/>.
    ///
    /// These are the only guards the vegetation presets have. Nothing else in the project
    /// reads them before they render: the emitter accepts any number for any field, the F1
    /// editor writes whatever the operator types straight into the asset, and the result is
    /// only visible by standing in the world at the right time of day. Every assertion below
    /// exists because the opposite state SHIPPED at some point and nobody noticed for weeks.
    ///
    /// The fixture resolves its subject through <see cref="ParticlePresetCategory.Of(string)"/>
    /// rather than a hard-coded id list, so a tenth vegetation preset added tomorrow is held
    /// to the same bar the day it lands.
    ///
    /// Read-only throughout: the presets are loaded from the real catalog via AssetDatabase
    /// and never mutated, never marked dirty, and never instantiated as scene objects.
    /// </summary>
    [TestFixture]
    public class PlantsPresetDataTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>World PPU for everything that is not a building or a tile (CLAUDE.md).</summary>
        private const float WORLD_PPU = 16f;

        /// <summary>
        /// Floor for a quad that has to show a SHAPE. One world unit is <see cref="WORLD_PPU"/>
        /// art texels, so 2 px is 2 / 16 = 0.125 world units. Under two texels a quad has no
        /// interior on either axis — the Leaf's tip, midrib and taper and the Petal's rim all
        /// fall inside a single texel, and after the texture's own alpha falloff the visible
        /// footprint is well under one.
        /// </summary>
        private const float MIN_SILHOUETTE_PIXELS = 2f;

        /// <summary>
        /// Floor for an ADDITIVE point of light: 1 texel, 1 / 16 = 0.0625 world units.
        ///
        /// This is a second floor, not a relaxation of the first, because it holds a
        /// different kind of particle. An alpha-blended quad contributes exactly as much
        /// colour as it COVERS, so a sub-texel one is arithmetically invisible no matter what
        /// silhouette it draws — that is the defect <see cref="MIN_SILHOUETTE_PIXELS"/> exists
        /// to catch. An additive quad contributes light regardless of coverage: it still reads
        /// as a glint at a size where an alpha-blended blob has already vanished, which is
        /// precisely the job of the Spark and Star pollen layers. They have no silhouette to
        /// resolve, and the cost audit measured them at 0.03 sq-units of overdraw, so holding
        /// them to the silhouette floor buys back no frames and only makes the glint coarser.
        ///
        /// One WHOLE texel is where it stops, though: below that the quad is finer than the
        /// pixel grid sampling it, so it scintillates in and out as the camera pans. That
        /// reads as flicker, not as sparkle, and no amount of additive brightness fixes it.
        /// </summary>
        private const float MIN_ADDITIVE_POINT_PIXELS = 1f;

        /// <summary>
        /// Steady-state live particles allowed per placed preset (Little's Law:
        /// emitRate x lifespan). The Plants presets are placed by the hundred — the shipped
        /// world carries 84 falling_leaf_30s and 57 flowers_pollen_soft — so the per-preset
        /// number multiplies straight into the frame budget.
        /// </summary>
        private const float MAX_LIVE_PARTICLES = 40f;

        /// <summary>Ids that must be present, so a rename cannot empty the fixture silently.</summary>
        private static readonly string[] ExpectedVegetationIds =
        {
            "falling_leaf_30s", "falling_leaf_canopy", "autumn_leaves_gradient",
            "falling_petal_30s", "flowers_petal_pink_60s", "flowers_pollen_soft",
            "flowers_pollen_drift_add", "flowers_pollen_glints_add", "flowers_pollen_haze_soft",
        };

        // Nothing here creates scene objects or textures, but the list + TearDown stay so a
        // future test that does cannot leak one: Domain Reload is OFF in this project, so a
        // survivor is carried into every later fixture in the run.
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── Fixture helpers ──────────────────────────────────────────────────────

        private static ParticlePresetCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsTrue(catalog != null, $"ParticlePresetCatalog not found at {CATALOG_PATH}.");
            return catalog;
        }

        /// <summary>Every catalog preset the F1 Plants tab shows, resolved by category.</summary>
        private static List<ParticlePresetDefinition> Vegetation()
        {
            var result = new List<ParticlePresetDefinition>();
            foreach (var p in LoadCatalog().Presets)
            {
                if (p == null || p.vfx == null) continue;
                if (ParticlePresetCategory.Of(p) == Cat.Vegetation) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Depth value of a sorting layer, read from the live
        /// <see cref="SortingLayer.layers"/> list so the comparison follows whatever order
        /// ProjectSettings > Tags and Layers actually holds. <c>int.MinValue</c> means the
        /// name is not a sorting layer at all.
        /// </summary>
        private static int SortingValueOf(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return int.MinValue;
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == layerName) return layers[i].value;
            return int.MinValue;
        }

        /// <summary>
        /// Particles alive at once. A looping emitter settles at emitRate x lifespan
        /// (Little's Law); a one-shot burst peaks at its whole <c>count</c>.
        /// </summary>
        private static float LiveParticles(ParticleVfxParams v)
            => v.loops
                ? Mathf.Max(0f, v.emitRate) * Mathf.Max(0f, v.lifespan)
                : Mathf.Max(0, v.count);

        /// <summary>
        /// The shape the emitter will actually draw. <c>textureShape</c> is
        /// <see cref="ParticleTextureShape.Auto"/> on every preset that never chose one, and
        /// Auto is resolved from kind + blend mode — so reading the raw field would classify a
        /// preset by a value that never reaches the texture library.
        /// </summary>
        private static ParticleTextureShape ResolvedShape(ParticleVfxParams v)
            => ParticleTextureLibrary.ResolveShape(v.textureShape, v.kind, v.additive);

        /// <summary>
        /// Which size floor a preset answers to, decided from the preset's OWN data.
        ///
        /// The blend mode is the discriminator, because it decides whether size and visibility
        /// are the same question. Alpha blending weights the particle by its coverage, so a
        /// quad narrower than a texel is invisible whatever it draws — it needs
        /// <see cref="MIN_SILHOUETTE_PIXELS"/>. Additive blending adds light irrespective of
        /// coverage, so a Spark or Star mote still reads at a size where an alpha quad would
        /// not; it answers to <see cref="MIN_ADDITIVE_POINT_PIXELS"/>.
        ///
        /// Leaf and Petal override that: they are the only shapes in the library with an
        /// outline to read, and an outline has to be resolvable however it is blended. An
        /// additive leaf would still be judged as a silhouette.
        ///
        /// The shipped pollen set is what settles blend mode over shape as the discriminator.
        /// <c>flowers_pollen_blue_drift_add</c> and <c>flowers_pollen_black_drift_soft</c> are
        /// the SAME Spark texture at the SAME sizeMax and differ only in blending — because
        /// additive black adds nothing, so a black mote has no choice but to be alpha. Judged
        /// by shape they would share a floor; judged by blending, the black one is correctly
        /// held to the size it needs in order to cover anything at all.
        /// </summary>
        private static bool IsSilhouette(ParticleVfxParams v)
        {
            var shape = ResolvedShape(v);
            if (shape == ParticleTextureShape.Leaf || shape == ParticleTextureShape.Petal)
                return true;

            return !v.additive;
        }

        /// <summary>The arithmetic behind <see cref="LiveParticles"/>, for failure messages.</summary>
        private static string LiveParticlesFormula(ParticleVfxParams v)
            => v.loops
                ? $"emitRate {v.emitRate} x lifespan {v.lifespan}"
                : $"one-shot burst of {v.count}";

        private static string Id(ParticlePresetDefinition p)
            => string.IsNullOrEmpty(p.id) ? p.name : p.id;

        // ── The picker's own label arithmetic ────────────────────────────────────

        /// <summary>
        /// <c>ParticlesRuntimeEditor.PICKER_LABEL_MIN_CHARS</c>, read off the editor rather
        /// than copied. This is the FLOOR of <c>PickerLabelBudget()</c>, which otherwise
        /// spends whatever the live GridLayoutGroup cell affords (roughly 12 characters at a
        /// 64 px cell, 19 at 96) — so the floor is the NARROWEST tile the author can ever be
        /// shown, and therefore the worst case these display names have to survive.
        /// </summary>
        private static int PickerLabelBudgetFloor()
        {
            var f = typeof(ParticlesRuntimeEditor).GetField(
                "PICKER_LABEL_MIN_CHARS", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(f,
                "ParticlesRuntimeEditor.PICKER_LABEL_MIN_CHARS is gone. This fixture reads the " +
                "picker's own floor instead of restating it, because the last hand-written copy " +
                "of this arithmetic was off by one and quietly compared the wrong string.");

            return (int)f.GetValue(null);
        }

        /// <summary>
        /// What the picker tile actually reads, produced by INVOKING the picker's own
        /// <c>TruncateName</c> rather than modelling it.
        ///
        /// The rule is <c>name.Length &lt;= max ? name : name.Substring(0, max - 1) + "…"</c>:
        /// at the floor budget of 9 a name of nine characters or fewer survives WHOLE, and
        /// only a longer one is cut — to its first eight characters plus an ellipsis. A
        /// previous version of this fixture cut everything at eight, so "Leaf Fall" (exactly
        /// nine) was compared as "Leaf Fal" — a string that never appears on screen.
        /// </summary>
        private static string PickerLabel(string displayName, int budget)
        {
            var m = typeof(ParticlesRuntimeEditor).GetMethod(
                "TruncateName", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(string), typeof(int) }, null);

            Assert.IsNotNull(m,
                "ParticlesRuntimeEditor.TruncateName(string, int) is gone. It is the single " +
                "function that decides what a picker tile says; a test that re-implements it " +
                "asserts against a picker that does not exist.");

            return (string)m.Invoke(null, new object[] { displayName, budget });
        }

        // ── Fixture sanity ───────────────────────────────────────────────────────

        /// <summary>
        /// GUARD FOR EVERY OTHER TEST IN THIS FILE. Category is decided by an ordered prefix
        /// table, and an unmatched id falls through to SpellFx instead of erroring — so
        /// renaming <c>flowers_pollen_soft</c> to <c>pollen_field</c> would move it out of
        /// Vegetation, out of the Plants tab, and out of every invariant below, all of which
        /// would then pass over an empty set. If this test is the only red one, the beauty
        /// guards did not break: they stopped running.
        /// </summary>
        [Test]
        public void PlantsTab_StillResolvesEveryVegetationPreset_SoTheseGuardsAreNotVacuous()
        {
            var veg = Vegetation();
            Assert.Greater(veg.Count, 0,
                "No preset classifies as Vegetation — the Plants tab is empty and every " +
                "invariant in this file is now checking nothing.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in veg) ids.Add(Id(p));

            foreach (string expected in ExpectedVegetationIds)
                Assert.IsTrue(ids.Contains(expected),
                    $"'{expected}' no longer reaches the Plants tab. Either it left the " +
                    "catalog or its id stopped matching a ParticlePresetCategory prefix, " +
                    "in which case it silently fell through to the SpellFx bucket.");
        }

        // ── Visibility ───────────────────────────────────────────────────────────

        /// <summary>
        /// NO SUB-PIXEL PARTICLES — measured against the floor that belongs to the particle,
        /// because the Plants tab holds two kinds and one floor cannot judge both.
        ///
        /// The defect this catches shipped: <c>falling_petal_30s</c> and
        /// <c>flowers_petal_pink_60s</c> ran at sizeMin 0.0625 / sizeMax 0.09375 world units —
        /// 1.0 to 1.5 px at 16 PPU — across 70 live particles each. The world was paying for
        /// cherry blossom nobody could see.
        ///
        /// A single floor of 2 px then over-corrected in the other direction: it condemned
        /// <c>flowers_pollen_drift_add</c>, an ADDITIVE Spark glint, for being 1.20 texels
        /// across. That preset is not failed cherry blossom, it is a mote of light — it has no
        /// silhouette to resolve and its whole layer measured 0.03 sq-units of overdraw, so
        /// nothing was being paid for and nothing was invisible. Raising it to clear a floor it
        /// had no business answering to left the assertion passing by 0.08 px, which is a
        /// coincidence rather than a margin.
        ///
        /// So the class is derived per preset by <see cref="IsSilhouette"/>, from the preset's
        /// own blend mode and resolved texture shape — never from a list of ids, so the tenth
        /// vegetation preset is judged correctly the day it lands.
        /// </summary>
        [Test]
        public void EveryPlantsPreset_IsLargerThanAPixelOfDust_OrItCostsFramesForNothing()
        {
            foreach (var p in Vegetation())
            {
                var v = p.vfx;
                float px = v.sizeMax * WORLD_PPU;
                var shape = ResolvedShape(v);

                if (IsSilhouette(v))
                {
                    Assert.GreaterOrEqual(px, MIN_SILHOUETTE_PIXELS,
                        $"'{Id(p)}' is alpha-blended {shape} and peaks at sizeMax " +
                        $"{v.sizeMax} world units — {v.sizeMax} x {WORLD_PPU} PPU = " +
                        $"{px:F2} art texels across, under the {MIN_SILHOUETTE_PIXELS} px " +
                        $"silhouette floor ({MIN_SILHOUETTE_PIXELS} / {WORLD_PPU} PPU = " +
                        $"{MIN_SILHOUETTE_PIXELS / WORLD_PPU} world units). Alpha blending " +
                        "weights a particle by what it COVERS, and under two texels this " +
                        "quad has no interior on either axis — a Leaf's tip, taper and " +
                        "midrib, or a Glow's core and skirt, all land inside a single texel, " +
                        "and after the shape's own falloff the visible footprint is under " +
                        $"one. {LiveParticles(v):F1} live particles per placed instance are " +
                        "simulated, drawn and blended to show nothing. Raise sizeMax to at " +
                        $"least {MIN_SILHOUETTE_PIXELS / WORLD_PPU} world units, switch the " +
                        "layer to additive if it is meant to be a mote of light, or delete " +
                        "it.");
                }
                else
                {
                    Assert.GreaterOrEqual(px, MIN_ADDITIVE_POINT_PIXELS,
                        $"'{Id(p)}' is an additive {shape} point of light and peaks at " +
                        $"sizeMax {v.sizeMax} world units — {v.sizeMax} x {WORLD_PPU} PPU = " +
                        $"{px:F2} art texels across, under the {MIN_ADDITIVE_POINT_PIXELS} px " +
                        $"additive floor ({MIN_ADDITIVE_POINT_PIXELS} / {WORLD_PPU} PPU = " +
                        $"{MIN_ADDITIVE_POINT_PIXELS / WORLD_PPU} world units). A glint is " +
                        "allowed to be smaller than a silhouette — additive light lands " +
                        "whatever the coverage — but under one whole texel the quad is finer " +
                        "than the pixel grid that samples it and scintillates as the camera " +
                        $"pans. That reads as flicker across {LiveParticles(v):F1} live " +
                        $"particles, not as sparkle. Raise sizeMax to at least " +
                        $"{MIN_ADDITIVE_POINT_PIXELS / WORLD_PPU} world units, or delete the " +
                        "layer.");
                }

                Assert.Greater(v.sizeMin, 0f,
                    $"'{Id(p)}' can be born at size {v.sizeMin} — a zero-size particle is a " +
                    "frame of nothing followed by a pop.");
                Assert.LessOrEqual(v.sizeMin, v.sizeMax,
                    $"'{Id(p)}' has sizeMin {v.sizeMin} above sizeMax {v.sizeMax}; Unity " +
                    "reads this range as min..max and the authored intent is lost.");
            }
        }

        // ── Silhouette and tumble ────────────────────────────────────────────────

        /// <summary>
        /// A particle with no silhouette, no birth rotation and no tumble is confetti, not
        /// foliage. Until <see cref="ParticleTextureShape.Leaf"/> and
        /// <see cref="ParticleTextureShape.Petal"/> existed, every falling leaf and petal in
        /// Valkur was a radial SoftDot blob that faked tumbling by oscillating a square quad's
        /// width — a shape with no long axis cannot read as a leaf no matter how it moves.
        ///
        /// Subject is derived from <c>vfx.kind == "falling_leaf"</c>, the runtime recipe, so a
        /// new leaf or petal preset is covered without touching this test.
        /// </summary>
        [Test]
        public void EveryFallingLeafPreset_HasALongAxisSilhouette_AndActuallyTumbles()
        {
            int checkedCount = 0;

            foreach (var p in Vegetation())
            {
                var v = p.vfx;
                if (!string.Equals(v.kind, "falling_leaf", StringComparison.Ordinal)) continue;
                checkedCount++;

                Assert.IsTrue(
                    v.textureShape == ParticleTextureShape.Leaf ||
                    v.textureShape == ParticleTextureShape.Petal,
                    $"'{Id(p)}' is kind 'falling_leaf' but draws {v.textureShape}. Only Leaf " +
                    "and Petal have a tip, a taper and a long axis; every radial shape reads " +
                    "as a blob however it spins.");

                Assert.Greater(Mathf.Abs(v.startRotationJitterDegrees), 0f,
                    $"'{Id(p)}' is born with no rotation jitter, so every particle is the " +
                    "same billboard at the same angle — the drift reads as a repeated stamp " +
                    "sliding down the screen rather than as separate leaves.");

                bool spins = Mathf.Abs(v.rotationSpeedDegrees) > 0f;
                bool turnsOver = v.turnoverCycles > 0;
                Assert.IsTrue(spins || turnsOver,
                    $"'{Id(p)}' neither spins (rotationSpeedDegrees {v.rotationSpeedDegrees}) " +
                    $"nor turns over (turnoverCycles {v.turnoverCycles}). A flat thing falling " +
                    "without rotating about either axis is confetti on a wire.");
            }

            Assert.Greater(checkedCount, 0,
                "No vegetation preset uses kind 'falling_leaf' — either the family was " +
                "renamed or this test just stopped covering anything.");
        }

        // ── Gradients ────────────────────────────────────────────────────────────

        /// <summary>
        /// A preset with no authored colorOverLife / alphaOverLife falls back to the emitter's
        /// hard-coded fade, which is a single flat tint held for the whole life. That is why
        /// <c>autumn_leaves_gradient</c> — a preset with the word "gradient" in its own id —
        /// rendered as one flat tan: it shipped with neither curve, and so did
        /// <c>falling_petal_30s</c> and <c>falling_leaf_30s</c>.
        ///
        /// Two keys is the minimum that can express a change; the last assertion catches the
        /// other half of the same defect, a curve that exists but holds one colour.
        /// </summary>
        [Test]
        public void EveryPlantsPreset_AuthorsItsOwnColorAndAlphaOverLife_NotTheFlatFallback()
        {
            foreach (var p in Vegetation())
            {
                var v = p.vfx;

                Assert.IsTrue(v.colorOverLife != null && v.colorOverLife.Length >= 2,
                    $"'{Id(p)}' has {(v.colorOverLife == null ? 0 : v.colorOverLife.Length)} " +
                    "colorOverLife key(s). Under two the emitter falls back to a single flat " +
                    "tint for the whole lifetime — the preset renders as one colour.");

                Assert.IsTrue(v.alphaOverLife != null && v.alphaOverLife.Length >= 2,
                    $"'{Id(p)}' has {(v.alphaOverLife == null ? 0 : v.alphaOverLife.Length)} " +
                    "alphaOverLife key(s). Under two there is no fade in or out, so every " +
                    "particle pops into existence at full opacity and vanishes the same way.");

                bool varies = false;
                for (int i = 1; i < v.colorOverLife.Length; i++)
                    if (v.colorOverLife[i].color != v.colorOverLife[0].color) { varies = true; break; }

                Assert.IsTrue(varies,
                    $"'{Id(p)}' has {v.colorOverLife.Length} colorOverLife keys that are all " +
                    "the same colour — a gradient in name only, indistinguishable on screen " +
                    "from the flat fallback it was supposed to replace.");
            }
        }

        // ── Ambient light ────────────────────────────────────────────────────────

        /// <summary>
        /// Every particle material is built on 'Universal Render Pipeline/Particles/Unlit', so
        /// Light2D never touches these quads. DayNightCycle drives the global light down to
        /// roughly (0.20, 0.25, 0.45) at intensity 0.15 and the tilemap under them goes
        /// near-black — while leaves and pollen keep rendering at authored noon brightness,
        /// which at midnight reads as glowing vegetation floating over a dark world.
        ///
        /// The flag defaults to false, so this is the whole-category opt-in: a new Plants
        /// preset that forgets it is a preset that glows in the dark.
        /// </summary>
        [Test]
        public void EveryPlantsPreset_RespondsToAmbientLight_OrItGlowsAtMidnight()
        {
            foreach (var p in Vegetation())
                Assert.IsTrue(p.vfx.respondsToAmbientLight,
                    $"'{Id(p)}' has respondsToAmbientLight off, so it renders at noon " +
                    "brightness at every hour. Vegetation is placed by the hundred across the " +
                    "map; at night it becomes the brightest thing on screen.");
        }

        // ── Budget ───────────────────────────────────────────────────────────────

        /// <summary>
        /// AMBIENT BUDGET. A looping emitter's steady-state live count is emitRate x lifespan
        /// (Little's Law), and ambient vegetation is the one effect family placed by the
        /// hundred — the shipped world holds 84 falling_leaf_30s and 57 flowers_pollen_soft
        /// instances, so a preset's number is multiplied by two orders of magnitude before it
        /// reaches the frame.
        ///
        /// <c>autumn_leaves_gradient</c> shipped at 80 live particles: 10/s for 8 s, double
        /// this ceiling, for leaves the player walks past without looking at.
        ///
        /// The composite total is asserted as well, because one placed instance of a preset
        /// with layers spawns the root system AND every layer — the number that actually hits
        /// the frame is the sum, and nothing in the F1 editor displays it.
        /// </summary>
        [Test]
        public void EveryPlantsPreset_StaysUnderTheAmbientParticleBudget()
        {
            foreach (var p in Vegetation())
            {
                float own = LiveParticles(p.vfx);
                Assert.LessOrEqual(own, MAX_LIVE_PARTICLES,
                    $"'{Id(p)}' holds {own:F1} live particles at once " +
                    $"({LiveParticlesFormula(p.vfx)}), over the {MAX_LIVE_PARTICLES} " +
                    "ceiling. Cut emitRate or lifespan — ambient vegetation is placed by " +
                    "the hundred and this number multiplies.");

                if (p.layers == null || p.layers.Count == 0) continue;

                float total = own;
                foreach (var layer in p.layers)
                    if (layer != null && layer.vfx != null) total += LiveParticles(layer.vfx);

                Assert.LessOrEqual(total, MAX_LIVE_PARTICLES,
                    $"One placed '{Id(p)}' spawns {p.layers.Count} layer(s) alongside its " +
                    $"root, for {total:F1} live particles in total against a " +
                    $"{MAX_LIVE_PARTICLES} ceiling. The root alone is within budget " +
                    $"({own:F1}); the stack is not.");
            }
        }

        // ── Composites ───────────────────────────────────────────────────────────

        /// <summary>
        /// A composite's sub-layers must be layerOnly and the composite itself must not be.
        ///
        /// Placing <c>flowers_pollen_soft</c> spawns its three pollen layers with it. If a
        /// layer also has a placement tile in the F1 picker, an author who places both gets
        /// that layer twice — same position, double density, double cost — and no part of the
        /// UI says so, because from the outside it just looks like the pollen is thicker
        /// there. The inverse mistake is worse: marking the composite ROOT layerOnly removes
        /// the only tile that places the whole effect, and the stack becomes unreachable.
        ///
        /// Derived from each composite's own <c>layers</c> list, so a fourth pollen layer is
        /// covered the moment it is wired up.
        /// </summary>
        [Test]
        public void EveryPlantsComposite_HidesItsSubLayers_ButKeepsItsOwnPlacementTile()
        {
            int compositesChecked = 0;

            foreach (var p in Vegetation())
            {
                if (p.layers == null || p.layers.Count == 0) continue;
                compositesChecked++;

                Assert.IsFalse(p.layerOnly,
                    $"'{Id(p)}' is a composite root with {p.layers.Count} layer(s) but is " +
                    "marked layerOnly, so the picker gives it no tile — the whole stacked " +
                    "effect can no longer be placed at all.");

                for (int i = 0; i < p.layers.Count; i++)
                {
                    var layer = p.layers[i];
                    Assert.IsTrue(layer != null,
                        $"'{Id(p)}' layer slot {i} is empty; the emitter skips nulls, so this " +
                        "is a layer the author believes is rendering and is not.");

                    Assert.IsTrue(layer.layerOnly,
                        $"'{Id(layer)}' is a layer of '{Id(p)}' but is not marked layerOnly, " +
                        "so it also gets its own placement tile. Placing the composite and " +
                        "then that tile beside it doubles the layer silently — same position, " +
                        "twice the particles, nothing in the UI to explain it.");

                    Assert.IsTrue(layer.layers == null || layer.layers.Count == 0,
                        $"'{Id(layer)}' is a layer of '{Id(p)}' and carries layers of its " +
                        "own. Composites are one level deep — the emitter ignores a layer's " +
                        "layers, so those nested systems never render.");
                }
            }

            Assert.Greater(compositesChecked, 0,
                "No vegetation preset has layers any more — the pollen stack was flattened " +
                "or unwired, and this test now covers nothing.");
        }

        // ── Depth ────────────────────────────────────────────────────────────────

        /// <summary>
        /// An authored sorting layer that does not exist in ProjectSettings is worse than none:
        /// <see cref="ParticleEmitter"/> validates the name against
        /// <see cref="SortingLayer.layers"/> and falls back to VFX, which puts the preset right
        /// back in front of the player — the exact bug the field was added to fix — while the
        /// asset reads as if it were fixed. The warning fires once per name per session and is
        /// easy to scroll past.
        ///
        /// An EMPTY name is the same fallback by design, so leaving it blank is not "the
        /// default"; for vegetation it is the defect.
        /// </summary>
        [Test]
        public void EveryPlantsPreset_NamesARealSortingLayer_AndNotTheVfxDefault()
        {
            foreach (var p in Vegetation())
            {
                string authored = p.vfx.sortingLayer;

                Assert.IsFalse(string.IsNullOrEmpty(authored),
                    $"'{Id(p)}' leaves sortingLayer empty, which ParticleEmitter resolves to " +
                    $"'{SortingConfig.LAYER_VFX}' — above Entities, Decorations, WallsTop and " +
                    "Projectiles. Every particle it emits draws in front of the player.");

                Assert.AreNotEqual(SortingConfig.LAYER_VFX, authored,
                    $"'{Id(p)}' is authored onto '{SortingConfig.LAYER_VFX}'. That layer is " +
                    "for spell effects that are meant to cover the fight; ambient vegetation " +
                    "on it draws over the player, every NPC and every wall top.");

                Assert.AreNotEqual(int.MinValue, SortingValueOf(authored),
                    $"'{Id(p)}' names sorting layer '{authored}', which does not exist in " +
                    "ProjectSettings > Tags and Layers. ParticleEmitter will silently fall " +
                    $"back to '{SortingConfig.LAYER_VFX}' and the preset renders in front of " +
                    "everything while the asset claims otherwise.");
            }
        }

        /// <summary>
        /// FALLING FOLIAGE MUST NOT HIDE BEHIND BUILDINGS.
        ///
        /// This replaced an earlier invariant that read "everything except the near plane draws
        /// BEHIND Entities". That rule was written before anyone checked what sits between the
        /// two: BuildingObject puts a building's body on WallsBottom and its canopy on WallsTop,
        /// and WallsBottom is the layer immediately below Entities. So "behind the player" also
        /// meant "behind every wall in the game", and the whole falling family disappeared
        /// inside towns.
        ///
        /// There is NO sorting layer between WallsBottom and Entities, so "behind the player"
        /// and "in front of a building body" cannot both be true through layers alone. That is
        /// a real trade-off with no correct answer for every scene, which is why the choice now
        /// belongs to the author through the DEPTH rows in the F1 Properties panel.
        ///
        /// What this test pins is only the half that is not a matter of taste: anything of kind
        /// falling_leaf must resolve ABOVE WallsBottom. The UPPER end is deliberately left
        /// unconstrained — putting a preset in front of everything is now a legitimate authored
        /// choice, and a test that forbade it would take back the control the panel just gave.
        ///
        /// Ground-level pollen is exempt and listed explicitly: it is haze at flower height, and
        /// a building standing in front of it SHOULD occlude it.
        ///
        /// Positions come from the live <see cref="SortingLayer.layers"/> ordering, so the test
        /// keeps its meaning if the stack is re-ordered in ProjectSettings.
        /// </summary>
        [Test]
        public void FallingFoliage_DrawsAboveTheBuildingBodyLayer_OrItVanishesInsideTowns()
        {
            int wallsBottom = SortingValueOf(SortingConfig.LAYER_WALLS_BOTTOM);
            Assert.AreNotEqual(int.MinValue, wallsBottom,
                $"Sorting layer '{SortingConfig.LAYER_WALLS_BOTTOM}' is missing from " +
                "ProjectSettings > Tags and Layers; depth cannot be reasoned about at all.");

            var hidden = new List<string>();

            foreach (var p in Vegetation())
            {
                // Ground haze is meant to be occluded by anything standing in front of it.
                if (p.vfx.kind != "falling_leaf") continue;

                int value = SortingValueOf(p.vfx.sortingLayer);
                if (value == int.MinValue) continue;   // reported by the unknown-layer test
                if (value <= wallsBottom) hidden.Add(Id(p));
            }

            CollectionAssert.IsEmpty(hidden,
                "These falling-foliage presets sit at or below " +
                $"'{SortingConfig.LAYER_WALLS_BOTTOM}', which is where BuildingObject draws a " +
                $"building's body: [{string.Join(", ", hidden)}]. Every leaf they emit is " +
                "swallowed by the first wall it falls past. Pick a layer above it in the " +
                "Properties panel's DEPTH section.");
        }

        // ── Picker labels ────────────────────────────────────────────────────────

        /// <summary>
        /// DISPLAY NAMES MUST DISAMBIGUATE AFTER TRUNCATION. The F1 picker cuts every tile
        /// label with <c>TruncateName(displayName, PickerLabelBudget())</c>. That budget is
        /// responsive — it spends the live GridLayoutGroup cell — but it is floored at
        /// <c>PICKER_LABEL_MIN_CHARS</c>, and the floor is the case that has to hold: it is the
        /// narrowest tile any author can be shown.
        ///
        /// At the floor of 9, TruncateName leaves a name of nine characters or fewer WHOLE and
        /// cuts anything longer to its first eight plus an ellipsis. Both halves of that rule
        /// matter here — "Leaf Fall" is exactly nine and reaches the tile intact — so the
        /// fixture invokes the picker's own function instead of restating it. The previous
        /// version restated it one character short and compared "Leaf Fal", a label the picker
        /// never draws.
        ///
        /// The Plants tab shipped with four tiles reading "Falling " character-for-character,
        /// plus "Flowers " on another four: nine presets, two legible names. Uniqueness of the
        /// full displayName is not enough, because none of it reaches the screen.
        /// </summary>
        [Test]
        public void PlantsDisplayNames_StayDistinctAfterThePickerTruncatesThem()
        {
            int budget = PickerLabelBudgetFloor();
            Assert.Greater(budget, 1,
                $"A label budget of {budget} would make TruncateName's Substring(0, max - 1) " +
                "degenerate; the picker cannot show a tile at that width at all.");

            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Vegetation())
            {
                string name = p.displayName;
                Assert.IsFalse(string.IsNullOrWhiteSpace(name),
                    $"'{Id(p)}' has no displayName, so its picker tile falls back to the raw " +
                    "id — the one string the picker was built to stop showing.");

                string visible = PickerLabel(name, budget);

                Assert.IsFalse(seen.ContainsKey(visible),
                    $"'{Id(p)}' (\"{name}\") and " +
                    $"'{(seen.ContainsKey(visible) ? seen[visible] : "")}' both render as " +
                    $"\"{visible}\" on their picker tiles — that is everything the narrowest " +
                    $"cell shows, at the {budget}-character floor of PickerLabelBudget(). Two " +
                    "Plants tiles that read the same are two tiles the author has to place and " +
                    "undo to tell apart.");

                seen[visible] = Id(p);
            }
        }
    }
}
