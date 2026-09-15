using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.ParticlesEditor
{
    /// <summary>ParticleSortingAuthoringTests: the writer and renderer tests. SetUp, TearDown and helpers live in ParticleSortingAuthoringTests.cs.</summary>
    public partial class ParticleSortingAuthoringTests
    {
        // ═══════════════════════════════════════════════════ what the writer stores

        /// <summary>
        /// Sorting-layer IDs are not stable across an edit to Tags and Layers, and presets are
        /// hand-authored data that has to survive one. If this fails the preset stores an index
        /// ("6") or an ID instead of a name, and the day someone reorders the layer list every
        /// preset authored through F1 silently moves.
        /// </summary>
        [Test]
        public void Writer_SortingLayerIndex_StoresTheLayerNAME_NotTheIndexOrTheId()
        {
            string target = FirstLayerAbove(SortingConfig.LAYER_WALLS_BOTTOM);
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_name")));
            int idx = RequireOptionIndex(dd, target);

            var def = MakePreset("__depth_name_probe");
            string stored = WriteLayerIndex(def, idx, out bool ok, out string err);

            Assert.IsTrue(ok, err);
            Assert.AreEqual(target, stored,
                "The field must hold the layer NAME. Anything numeric here is an index or an " +
                "ID, and neither survives a reorder of ProjectSettings > Tags and Layers.");
        }

        /// <summary>
        /// <summary>
        /// An empty sortingLayer still resolves to VFX at the renderer.
        ///
        /// This is the guarantee that protects the roughly 130 shipped presets that author
        /// nothing at all. Before the field existed, ParticleEmitter hard-coded VFX for every
        /// system it ever built; if an empty value ever stopped resolving there, all of them
        /// would re-layer at once, with no error anywhere and nothing in the panel to show it.
        ///
        /// Deliberately asserted on the FIELD rather than through a dropdown entry: the list
        /// carries no "(unset)" option, because an empty value and "VFX" render identically
        /// and offering both would be two choices that do the same thing.
        /// </summary>
        [Test]
        public void EmptySortingLayer_StillResolvesToVfxAtTheRenderer()
        {
            var def = MakePreset("__depth_empty_rt", layer: _priorLayer);
            def.vfx.sortingLayer = "";

            var emitter = CreateEmitter();
            emitter.ApplyPreset(def, 1f);

            Assert.AreEqual(SortingConfig.LAYER_VFX, RootRendererOf(emitter).sortingLayerName,
                "An empty sortingLayer must still resolve to VFX at the renderer — that is the " +
                "value ParticleEmitter hard-coded for every system it ever built.");
        }

        /// A dropdown index with no layer behind it can only arrive from a bug — a stale option
        /// list, a caller passing the wrong number. If this fails, that bug reaches the user
        /// either as an exception swallowed by the UI event system, or as a preset silently
        /// holding a name ProjectSettings has never heard of: the emitter then warns once and
        /// draws on VFX, in front of the player, the walls and the canopy, with nothing on
        /// screen explaining why.
        /// </summary>
        [TestCase(9999)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void Writer_OutOfRangeLayerIndex_NeitherThrowsNorLeavesAnUnresolvableName(int index)
        {
            var def = MakePreset("__depth_oob", layer: _priorLayer);

            bool ok = false;
            string err = null;
            Assert.DoesNotThrow(
                () => ok = ParticlePresetFieldWriter.TrySetField(def, KEY_LAYER, index, out err),
                "TrySetField's contract is to report, never to throw: the caller is a UI " +
                "handler and an exception there is swallowed by the event system.");

            AssertLayerFieldIsResolvable(def, $"after writing out-of-range index {index}");
            if (ok)
                Assert.IsTrue(string.IsNullOrEmpty(def.vfx.sortingLayer),
                    $"Index {index} names no layer, so the only defensible thing to accept it " +
                    $"as is 'unset'. It stored '{def.vfx.sortingLayer}'.");
            else
                Assert.AreEqual(_priorLayer, def.vfx.sortingLayer,
                    "A REFUSED edit must leave the field exactly as it was. A half-applied " +
                    "rejection loses the layer the author had already chosen — and the panel " +
                    "rebuilds itself from the field right afterwards, so they would watch it " +
                    $"change. The writer said: '{err}'.");
        }

        /// <summary>
        /// The same contract for a value that is not a number at all. If this fails, a future
        /// widget change that starts emitting something else takes the depth field down with it
        /// instead of showing "Edit rejected" in the status line.
        /// </summary>
        [Test]
        public void Writer_NullLayerValue_NeitherThrowsNorCorruptsTheField()
        {
            var def = MakePreset("__depth_null", layer: _priorLayer);

            bool ok = false;
            string err = null;
            Assert.DoesNotThrow(
                () => ok = ParticlePresetFieldWriter.TrySetField(def, KEY_LAYER, null, out err),
                "A null from a widget must be reported, not thrown.");

            AssertLayerFieldIsResolvable(def, "after writing null");
            if (!ok)
                Assert.AreEqual(_priorLayer, def.vfx.sortingLayer,
                    $"A refused edit must not half-apply. The writer said: '{err}'.");
        }

        /// <summary>
        /// The two numeric depth rows, round-tripped in the types PropertyForm actually emits
        /// (AddInt sends int, AddFloat sends float). If this fails the rows are inert: the
        /// author types a value, the status line says it was applied, and the preset keeps the
        /// old one.
        /// </summary>
        [Test]
        public void Writer_OrderAndFudge_RoundTripInTheTypesTheRowsEmit()
        {
            var def = MakePreset("__depth_numeric");

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(def, KEY_ORDER, 7, out var e1), e1);
            Assert.AreEqual(7, def.vfx.sortingOrder, "The Sorting Order row must reach the field.");

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(def, KEY_FUDGE, -0.25f, out var e2), e2);
            Assert.AreEqual(-0.25f, def.vfx.sortingFudge, 1e-5f,
                "The Sorting Fudge row must reach the field with its sign and its fraction " +
                "intact — LOWER fudge draws in front, so a dropped minus sign inverts the " +
                "author's intent rather than merely weakening it.");
        }

        /// <summary>
        /// If this fails, typing nonsense into the order row zeroes the order the preset had.
        /// The panel rebuilds from the field immediately afterwards, so the author's real value
        /// disappears in front of them while the status line claims success.
        /// </summary>
        [Test]
        public void Writer_UnparseableOrder_IsRejectedAndLeavesTheFieldAlone()
        {
            var def = MakePreset("__depth_bad_order");
            def.vfx.sortingOrder = 12;

            bool ok = false;
            string err = null;
            Assert.DoesNotThrow(
                () => ok = ParticlePresetFieldWriter.TrySetField(def, KEY_ORDER, "not a number", out err));

            Assert.IsFalse(ok, "An unparseable order must be reported as rejected.");
            Assert.IsFalse(string.IsNullOrEmpty(err), "The status line needs something to say.");
            Assert.AreEqual(12, def.vfx.sortingOrder, "A rejected edit must not touch the field.");
        }

        // ═════════════════════════════════════════════ round trip to the renderer

        /// <summary>
        /// THE assertion that would have caught the original defect, where the depth values
        /// were stored on the preset and nothing re-applied them to the renderer. If this
        /// fails, the panel is a placebo: the author picks a layer, the preset saves it, the
        /// table shows it, and the effect on screen never moves.
        /// </summary>
        [Test]
        public void AuthoredLayer_ReachesTheParticleSystemRenderer_ThroughApplyPreset()
        {
            string target = FirstLayerAbove(SortingConfig.LAYER_WALLS_BOTTOM);
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_to_renderer")));
            int idx = RequireOptionIndex(dd, target);

            var def = MakePreset("__depth_to_renderer_probe");
            string stored = WriteLayerIndex(def, idx, out bool ok, out string err);
            Assert.IsTrue(ok, err);

            var emitter = CreateEmitter();
            emitter.ApplyPreset(def, 1f);

            Assert.AreEqual(stored, RootRendererOf(emitter).sortingLayerName,
                "What the panel wrote must be what the renderer draws on. A value that is " +
                "stored but never re-applied is the exact shape of the bug these rows fix.");
        }

        /// <summary>
        /// Every entry the panel offers, all the way through. If this fails for one entry, that
        /// entry is a trap: selecting it looks like it worked and puts the effect somewhere
        /// else. For a name the project does not define it is worse — assigning one straight to
        /// sortingLayerName throws, which is why ParticleEmitter validates against
        /// SortingLayer.layers before it assigns anything.
        /// </summary>
        [Test]
        public void EveryEntryTheDropdownOffers_SurvivesAllTheWayToTheRenderer()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_all_entries")));
            var def = MakePreset("__depth_all_entries_probe");
            var emitter = CreateEmitter();   // reused: ApplyPreset rebuilds every module

            for (int i = 0; i < dd.options.Count; i++)
            {
                string label = dd.options[i].text;
                string stored = WriteLayerIndex(def, i, out bool ok, out string err);
                Assert.IsTrue(ok, $"Entry {i} ('{label}') was refused: {err}");

                emitter.ApplyPreset(def, 1f);

                string expected = string.IsNullOrEmpty(stored) ? SortingConfig.LAYER_VFX : stored;
                string drawnOn = RootRendererOf(emitter).sortingLayerName;
                Assert.AreEqual(expected, drawnOn,
                    $"Entry {i} ('{label}') stored '{stored}' but the renderer drew on " +
                    $"'{drawnOn}'.");
            }
        }

        /// <summary>
        /// Order and fudge take the same trip as the layer. If this fails, the co-located
        /// systems of a composite preset keep tie-breaking on Unity's internal order however
        /// the author sets them — the additive rim over the soft core, the sparks over the
        /// haze, all of it stays a coin flip.
        /// </summary>
        [Test]
        public void AuthoredOrderAndFudge_ReachTheRendererToo_NotJustTheLayer()
        {
            var def = MakePreset("__depth_numeric_to_renderer");
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(def, KEY_ORDER, 5, out var e1), e1);
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(def, KEY_FUDGE, -0.5f, out var e2), e2);

            var emitter = CreateEmitter();
            emitter.ApplyPreset(def, 1f);
            var r = RootRendererOf(emitter);

            Assert.AreEqual(5, r.sortingOrder, "The authored order must reach the renderer.");
            Assert.AreEqual(-0.5f, r.sortingFudge, 1e-4f,
                "The authored fudge must reach the renderer — it is the only separation two " +
                "co-located systems sharing a layer and an order have.");
        }

        // ═════════════════════════════════════════════════════ the shipped regression

        /// <summary>
        /// The premise of the bug, pinned so the story stays readable: the vegetation presets
        /// were authored onto ObjectsLow, and the project draws ObjectsLow BEHIND WallsBottom,
        /// where buildings render. If this fails the layer order has been rearranged, and every
        /// comment in this area — plus the fallback reasoning inside ParticleEmitter — needs
        /// re-reading before it is trusted again.
        /// </summary>
        [Test]
        public void ProjectLayerOrder_StillDrawsObjectsLowBelowWallsBottom_ThePremiseOfTheLeafBug()
        {
            int objectsLow = RequireDrawOrderIndex(SortingConfig.LAYER_OBJECTS_LOW);
            int wallsBottom = RequireDrawOrderIndex(SortingConfig.LAYER_WALLS_BOTTOM);

            Assert.Less(objectsLow, wallsBottom,
                "ObjectsLow must still draw behind WallsBottom. That relationship is why every " +
                "falling leaf placed on ObjectsLow hid behind every building wall, and it is " +
                "what the depth rows exist to let an author escape.");
        }

        /// <summary>
        /// The regression itself, stated as a relationship rather than as two literal names: a
        /// preset authored through the panel onto a layer the project draws in front of
        /// WallsBottom must actually END UP in front of WallsBottom at the renderer. If this
        /// fails, the leaves are behind the walls again — either because the panel wrote
        /// something the emitter could not resolve (so it fell back), or because nothing
        /// re-applied the value at all.
        /// </summary>
        [Test]
        public void PresetAuthoredAboveWallsBottom_ResolvesAboveIt_SoTheLeafDrawsInFrontOfTheWall()
        {
            int wallsBottom = RequireDrawOrderIndex(SortingConfig.LAYER_WALLS_BOTTOM);
            string above = FirstLayerAbove(SortingConfig.LAYER_WALLS_BOTTOM);

            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_regression")));
            int idx = RequireOptionIndex(dd, above);

            var def = MakePreset("__depth_regression_probe");
            string stored = WriteLayerIndex(def, idx, out bool ok, out string err);
            Assert.IsTrue(ok, err);

            var emitter = CreateEmitter();
            emitter.ApplyPreset(def, 1f);

            string drawnOn = RootRendererOf(emitter).sortingLayerName;
            int drawn = RequireDrawOrderIndex(drawnOn);

            Assert.Greater(drawn, wallsBottom,
                $"Authored '{above}' (draw order {DrawOrderIndex(above)}), stored '{stored}', " +
                $"drew on '{drawnOn}' (draw order {drawn}) — which is not in front of " +
                $"WallsBottom (draw order {wallsBottom}). Buildings render on WallsBottom / " +
                "WallsTop, so this is the falling leaf disappearing behind the wall it falls " +
                "past.");
        }
    }
}
