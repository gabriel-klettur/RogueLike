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

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// The AUTHORING loop for a preset's depth, end to end: F1 Properties row →
    /// <see cref="ParticlePresetFieldWriter"/> → <see cref="ParticleVfxParams.sortingLayer"/>
    /// → <see cref="ParticleEmitter.ApplyPreset"/> → <see cref="ParticleSystemRenderer"/>.
    ///
    /// <see cref="ParticleEmitterSortingAndAmbientTests"/> already pins the LAST link of that
    /// chain — that a preset which authors a layer reaches the renderer with it, and that one
    /// which authors nothing still lands on VFX at order 0. This fixture pins the links
    /// BEFORE it, because a value nothing can set is exactly as useless as a value nothing
    /// re-applies. Both halves have already failed in this game once:
    ///
    ///   • the vegetation presets were put on ObjectsLow, which the project draws BELOW
    ///     WallsBottom, so every falling leaf hid behind every building wall (BuildingObject
    ///     assigns WallsBottom / WallsTop by the instance's z offset);
    ///   • and the only way to move them off it was to open the .asset in the Inspector,
    ///     because the Properties panel offered no depth row at all.
    ///
    /// There is NO sorting layer between WallsBottom and Entities, so "behind the player" and
    /// "in front of a building body" cannot both be true through layers alone. That is
    /// precisely why the choice has to belong to the person authoring the preset — the right
    /// answer is per-preset and no hardcoded layer can be it.
    ///
    /// Nothing here hardcodes a layer NAME LIST. Every expectation about which layers exist,
    /// and about which of them draws in front of which, is derived from
    /// <see cref="SortingLayer.layers"/> at run time, so the suite survives an edit to
    /// ProjectSettings > Tags and Layers instead of turning red for it. Only two names are
    /// spelled out — <see cref="SortingConfig.LAYER_WALLS_BOTTOM"/> and
    /// <see cref="SortingConfig.LAYER_OBJECTS_LOW"/>, through the constants, never as
    /// literals — because the regression IS about those two.
    ///
    /// EditMode notes: <c>OnPresetPropertyChanged</c> deliberately early-returns outside Play
    /// Mode so a fixture cannot dirty a real .asset. These tests therefore drive the two
    /// halves it joins — <c>RebuildPresetPropertyForm</c> for what the panel OFFERS, and
    /// <c>ParticlePresetFieldWriter.TrySetField</c> for what an edit STORES — and push the
    /// result through <c>ApplyPreset</c> themselves.
    /// </summary>
    [TestFixture]
    public class ParticleSortingAuthoringTests
    {
        /// <summary>
        /// Row keys, built from the field names rather than typed out: renaming
        /// <see cref="ParticleVfxParams.sortingLayer"/> must break the build here, not
        /// silently turn every assertion below into "the row is missing".
        /// </summary>
        private const string VFX = "vfx.";
        private static readonly string KEY_LAYER = VFX + nameof(ParticleVfxParams.sortingLayer);
        private static readonly string KEY_ORDER = VFX + nameof(ParticleVfxParams.sortingOrder);
        private static readonly string KEY_FUDGE = VFX + nameof(ParticleVfxParams.sortingFudge);

        /// <summary>
        /// Parked in the field before an edit that is expected to be REFUSED, so "the field
        /// still holds this" proves the refusal did not half-apply. Deliberately a real layer
        /// name (resolved at run time) rather than a marker string: the invariant under test is
        /// that a rejected write leaves behind data the emitter can still resolve.
        /// </summary>
        private string _priorLayer;

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _trackedAssets = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            // Both halves of this fixture log in EditMode without anything being wrong:
            // building TMP rows outside a running Canvas, and building a ParticleSystem whose
            // renderer/material chatter would fail an assertion that passed.
            LogAssert.ignoreFailingMessages = true;

            _priorLayer = ProjectLayerNames()[0];
            ClearSortingLayerVerdictCache();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _trackedAssets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _trackedAssets.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();

            // Domain Reload is OFF and the only reset hook on this ledger is a
            // [RuntimeInitializeOnLoadMethod], which never fires in EditMode. Leaving it
            // populated would make ParticleEmitterSortingAndAmbientTests' warn-once test
            // order-dependent on whether this fixture ran first.
            ClearSortingLayerVerdictCache();

            LogAssert.ignoreFailingMessages = false;
        }

        // ═══════════════════════════════════════════════ the panel offers the rows

        /// <summary>
        /// If this fails, a preset's depth cannot be changed from the game at all: the author
        /// has to leave F1, hunt the .asset down in the Project window and edit three fields in
        /// the Inspector — which is how ~150 vegetation emitters ended up shipped on
        /// ObjectsLow, behind every wall in the game, with nobody able to fix them in place.
        /// </summary>
        [Test]
        public void PresetForm_OffersARowForEveryDepthField_OrDepthIsUnreachableOutsideTheInspector()
        {
            var form = BuildFormFor(MakePreset("__depth_rows"));
            var keys = FormFieldKeys(form);

            CollectionAssert.Contains(keys, KEY_LAYER,
                "The Properties form must offer a Sorting Layer row. Without it the only way " +
                "to move a preset off the layer it was imported onto is the Inspector.");
            CollectionAssert.Contains(keys, KEY_ORDER,
                "The Properties form must offer a Sorting Order row. Order is the only " +
                "separation available between two presets that share a layer.");
            CollectionAssert.Contains(keys, KEY_FUDGE,
                "The Properties form must offer a Sorting Fudge row. It is the ONLY tool that " +
                "can order the co-located systems of a composite preset against each other: " +
                "they land on the same layer at the same order and the instance loader pins " +
                "every emitter to z = 0, so without it their draw order is Unity's internal " +
                "tie-break instead of the author's choice.");
        }

        /// <summary>
        /// If this fails, the layer is typed by hand. A typo does not report itself where the
        /// author is looking — the emitter falls back to VFX and warns once per session — so
        /// the effect draws in front of everything and the spelling is the last suspect.
        /// </summary>
        [Test]
        public void PresetForm_SortingLayerRow_IsADropdown_SoAnUnknownLayerNameCannotBeTyped()
        {
            var form = BuildFormFor(MakePreset("__depth_widget"));

            var component = FormField(form, KEY_LAYER);
            Assert.IsInstanceOf<TMP_Dropdown>(component,
                "The Sorting Layer row must be a dropdown over the project's layers, not a " +
                "free-text field: at run time ParticleEmitter cannot tell a typo from a " +
                "deliberate choice, it can only fall back to VFX and warn.");
        }

        /// <summary>
        /// If this fails, the author cannot type the value the field needs: a fudge row
        /// restricted to whole numbers cannot express the sub-unit bias that is its entire
        /// purpose, and an order row that accepts decimals silently rounds what was typed.
        /// </summary>
        [Test]
        public void PresetForm_OrderRowTakesWholeNumbers_AndFudgeRowTakesDecimals()
        {
            var form = BuildFormFor(MakePreset("__depth_widget_types"));

            var order = FormField(form, KEY_ORDER) as TMP_InputField;
            Assert.IsTrue(order != null, "The Sorting Order row must be a text-entry row.");
            Assert.AreEqual(TMP_InputField.ContentType.IntegerNumber, order.contentType,
                "sortingOrder is an int. A decimal row here rounds on the way to the writer, " +
                "so what the author typed is not what the preset stores.");

            var fudge = FormField(form, KEY_FUDGE) as TMP_InputField;
            Assert.IsTrue(fudge != null, "The Sorting Fudge row must be a text-entry row.");
            Assert.AreEqual(TMP_InputField.ContentType.DecimalNumber, fudge.contentType,
                "sortingFudge is a float whose useful range is fractions of a world unit — a " +
                "row that refuses '0.5' cannot author the only kind of value it exists for.");
        }

        // ═══════════════════════════════════════════ the options are the project's

        /// <summary>
        /// If this fails, either a layer the project defines is unreachable from the panel (the
        /// author cannot put the preset where it belongs), or the panel offers a name the
        /// project no longer defines — and choosing that one resolves to VFX, which throws the
        /// effect in front of the entire world.
        /// </summary>
        [Test]
        public void SortingLayerOptions_OfferEveryProjectSortingLayerExactlyOnce()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_options")));
            var expected = ProjectLayerNames();

            var offered = new List<string>();
            var extras = new List<string>();
            SplitOptions(dd, offered, extras);

            // Equivalence, not sequence equality: which DIRECTION the ladder runs in is
            // SortingLayerOptions_ReadAsADepthLadder_NotAScrambledNameList's question, and
            // front-to-back reads as well as back-to-front. What this test refuses is a
            // MISSING layer, a duplicate, or a name Tags and Layers no longer defines.
            CollectionAssert.AreEquivalent(expected, offered,
                "The dropdown's layer entries must be exactly SortingLayer.layers, each " +
                "exactly once. The expectation is read from SortingLayer.layers at run time, " +
                "so this failing means the panel's list has drifted from Tags and Layers — not " +
                "that the layers changed. Offered: " + string.Join(", ", offered));
            Assert.LessOrEqual(extras.Count, 1,
                "At most one entry may be something other than a layer name — the 'unset' " +
                "entry that stores the empty default. Found: " + string.Join(", ", extras));
        }

        /// <summary>
        /// If this fails, the dropdown is a scrambled list of names. The author's whole
        /// question is "is this in front of the wall or behind it", and a list that does not
        /// read as the depth ladder turns that into trial and error.
        /// </summary>
        [Test]
        public void SortingLayerOptions_ReadAsADepthLadder_NotAScrambledNameList()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_ladder")));

            var offered = new List<string>();
            SplitOptions(dd, offered, new List<string>());
            Assert.Greater(offered.Count, 1, "Need at least two layers to have an order at all.");

            int first = DrawOrderIndex(offered[0]);
            int last = DrawOrderIndex(offered[offered.Count - 1]);
            int step = last > first ? 1 : -1;

            for (int i = 1; i < offered.Count; i++)
            {
                int prev = DrawOrderIndex(offered[i - 1]);
                int now = DrawOrderIndex(offered[i]);
                Assert.AreEqual(step, Math.Sign(now - prev),
                    $"'{offered[i - 1]}' then '{offered[i]}' breaks the ladder. The list must " +
                    "run through SortingLayer.layers in ONE direction — back-to-front or " +
                    "front-to-back, either reads — so that neighbouring entries are " +
                    "neighbouring depths.");
            }
        }

        /// <summary>
        /// The escape hatch from the shipped bug. If this fails, a leaf stuck behind a building
        /// wall still cannot be freed from the panel, whatever else the depth rows do.
        /// </summary>
        [Test]
        public void SortingLayerOptions_IncludeALayerAboveWallsBottom_SoTheLeafBehindTheWallIsFixable()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_above_walls")));
            int wallsBottom = RequireDrawOrderIndex(SortingConfig.LAYER_WALLS_BOTTOM);

            var offered = new List<string>();
            SplitOptions(dd, offered, new List<string>());

            bool any = false;
            foreach (var name in offered)
                if (DrawOrderIndex(name) > wallsBottom) { any = true; break; }

            Assert.IsTrue(any,
                "No entry in the dropdown draws in front of WallsBottom. Buildings render on " +
                "WallsBottom / WallsTop, so a panel that cannot reach past WallsBottom cannot " +
                "get a falling leaf out from behind a wall — which is the defect these rows " +
                "exist to fix.");
        }

        // ═══════════════════════════════════════════ the row and the writer agree

        /// <summary>
        /// If this fails, the row shows one layer while the preset holds another. The author
        /// opens a preset that was authored onto WallsTop, reads "Background", and every
        /// decision they make from there is made against a lie.
        /// </summary>
        [Test]
        public void SortingLayerRow_ShowsTheLayerThePresetAuthors_NotTheFirstEntry()
        {
            string authored = FirstLayerAbove(SortingConfig.LAYER_WALLS_BOTTOM);
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_selected", layer: authored)));

            Assert.GreaterOrEqual(dd.value, 0, "The dropdown must have a selection.");
            Assert.Less(dd.value, dd.options.Count, "The selection must be inside the option list.");
            Assert.AreEqual(authored, dd.options[dd.value].text,
                $"The preset authors '{authored}', so the row must show it. A row that always " +
                "shows the first entry makes every preset look unauthored and invites the " +
                "author to 'fix' one that was already right.");
        }

        /// <summary>
        /// <summary>
        /// An unauthored preset opens showing the layer it ACTUALLY draws on.
        ///
        /// The list deliberately carries no separate "(unset)" entry. An empty sortingLayer and
        /// the literal "VFX" resolve to the same renderer layer, so offering both would put two
        /// visibly different choices in one list that do exactly the same thing and leave the
        /// author to work that out. Instead an empty value preselects VFX, and the row therefore
        /// always states the truth about where the emitter draws.
        ///
        /// Nothing is baked by merely opening the panel: the form pushes a value only through
        /// ValueChanged, which a rebuild does not fire, so an untouched row writes nothing to
        /// the asset. Picking VFX by hand rewrites "" to "VFX", which is a render no-op.
        ///
        /// If this fails, an unauthored preset opens on some unrelated layer and invites the
        /// author to "fix" a preset that was already correct.
        /// </summary>
        [Test]
        public void SortingLayerRow_UnauthoredPreset_OpensOnTheLayerItActuallyDrawsOn()
        {
            var def = MakePreset("__depth_unset");
            Assert.AreEqual("", def.vfx.sortingLayer, "Fixture check: the probe starts unauthored.");

            var dd = LayerDropdown(BuildFormFor(def));

            Assert.GreaterOrEqual(dd.value, 0, "The dropdown must have a selection.");
            Assert.Less(dd.value, dd.options.Count, "The selection must be inside the option list.");
            Assert.AreEqual(SortingConfig.LAYER_VFX, dd.options[dd.value].text,
                "An unauthored preset resolves to VFX at the renderer, so that is what the row " +
                "must show. Entries offered: " + string.Join(", ", OptionLabels(dd)));
        }

        /// <summary>
        /// No two entries may mean the same thing.
        ///
        /// This is the invariant that replaced an explicit "(unset)" entry: a list where two
        /// rows store different values that render identically is a list the author cannot
        /// reason about. If this fails, someone has re-added a duplicate-meaning entry and the
        /// panel now offers a choice that is not a choice.
        /// </summary>
        [Test]
        public void SortingLayerDropdown_NoTwoEntriesStoreTheSameLayer()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_dupes")));
            var probe = MakePreset("__depth_dupe_probe");
            var seen = new Dictionary<string, int>();

            for (int i = 0; i < dd.options.Count; i++)
            {
                probe.vfx.sortingLayer = _priorLayer;
                string stored = WriteLayerIndex(probe, i, out bool ok, out _);
                if (!ok) continue;

                // "" and "VFX" are the same layer to the renderer, so compare on what the
                // emitter would resolve, not on the raw field.
                string resolved = string.IsNullOrEmpty(stored) ? SortingConfig.LAYER_VFX : stored;
                Assert.IsFalse(seen.ContainsKey(resolved),
                    $"Entries {seen.GetValueOrDefault(resolved, -1)} ('{dd.options[seen.GetValueOrDefault(resolved, 0)].text}') " +
                    $"and {i} ('{dd.options[i].text}') both end up on '{resolved}'.");
                seen[resolved] = i;
            }

            Assert.AreEqual(dd.options.Count, seen.Count,
                "Every entry must resolve to a distinct layer.");
        }

        /// The one lie a dropdown can tell that nothing else catches: entry 4 is labelled
        /// 'Entities' and stores 'Decorations'. Every preset authored through the panel would
        /// then sit one layer off, consistently, while the panel kept showing the label the
        /// author picked. This is the same rule the gradient rows already live under — a row
        /// that displays one key and writes another is a lie the author cannot see.
        /// </summary>
        [Test]
        public void SortingLayerDropdown_EveryEntryStoresTheLayerItIsLabelledWith()
        {
            var dd = LayerDropdown(BuildFormFor(MakePreset("__depth_labels")));
            var probe = MakePreset("__depth_label_probe");
            var layerNames = new List<string>(ProjectLayerNames());

            for (int i = 0; i < dd.options.Count; i++)
            {
                string label = dd.options[i].text;
                probe.vfx.sortingLayer = _priorLayer;
                string stored = WriteLayerIndex(probe, i, out bool ok, out string err);

                Assert.IsTrue(ok, $"Entry {i} ('{label}') was refused by the writer: {err}. " +
                                  "Every entry the panel offers must be selectable.");
                if (layerNames.Contains(label))
                    Assert.AreEqual(label, stored,
                        $"Entry {i} is labelled '{label}' but stored '{stored}'. The panel and " +
                        "ParticlePresetFieldWriter must resolve an index against the SAME " +
                        "list, or every preset authored through the panel lands on the wrong " +
                        "layer while the UI keeps showing the right one.");
                else
                    Assert.IsTrue(string.IsNullOrEmpty(stored),
                        $"Entry {i} ('{label}') is not one of the project's layers, so it can " +
                        "only be the unset entry — and the unset entry clears the field, which " +
                        $"ParticleEmitter resolves to VFX. It stored '{stored}'.");
            }
        }

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

        // ═══════════════════════════════════════════════════════════════════ fixtures

        /// <summary>
        /// A minimal continuous emitter preset. Everything this fixture never asserts on is
        /// held constant, so a failure can only be about depth.
        /// </summary>
        private ParticlePresetDefinition MakePreset(string id, string layer = "",
                                                    int order = 0, float fudge = 0f)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _trackedAssets.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = 10f,
                count = 6,
                lifespan = 0.5f,
                speed = 1f,
                sizeMin = 0.1f,
                sizeMax = 0.2f,
                sortingLayer = layer,
                sortingOrder = order,
                sortingFudge = fudge,
                color = Color.white,
                colors = new[] { Color.white },
            };
            return def;
        }

        private ParticlePresetCatalog MakeCatalogWith(ParticlePresetDefinition def)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _trackedAssets.Add(catalog);
            catalog.SetPresets(new List<ParticlePresetDefinition> { def });
            return catalog;
        }

        private ParticleEmitter CreateEmitter(string name = "DepthAuthoringTestEmitter")
        {
            var go = new GameObject(name);
            _sceneObjects.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        /// <summary>
        /// Runs the real <c>RebuildPresetPropertyForm</c> against a real
        /// <see cref="PropertyForm"/> and hands back the form it filled.
        ///
        /// A fresh editor and a fresh form per call, on purpose: <c>PropertyForm.Clear</c> uses
        /// <c>Object.Destroy</c>, which is deferred (and complains) in EditMode, so rebuilding
        /// twice into one form would leave the first build's rows in the hierarchy. Building
        /// once into an empty form keeps that whole question out of the assertions.
        /// </summary>
        private PropertyForm BuildFormFor(ParticlePresetDefinition def)
        {
            var editor = CreateEditor();
            SetFieldValue(editor, "_catalog", MakeCatalogWith(def));

            var form = CreateStandaloneForm();

            // UIRefs is a struct, so it has to be boxed, poked and written back — assigning
            // through the FieldInfo directly would mutate a copy and the editor would still
            // see a null form.
            var uiField = FindField(editor, "_ui");
            Assert.IsNotNull(uiField, "ParticlesRuntimeEditor._ui is gone — this fixture " +
                                      "injects the Properties form through it.");
            object boxedUi = uiField.GetValue(editor);
            var formField = boxedUi.GetType().GetField("PresetPropsForm");
            Assert.IsNotNull(formField, "UIRefs.PresetPropsForm is gone — the Properties form " +
                                        "is no longer reachable from the editor's UI refs.");
            formField.SetValue(boxedUi, form);
            uiField.SetValue(editor, boxedUi);

            InvokeMethod(editor, "RebuildPresetPropertyForm", def.id);
            return form;
        }

        /// <summary>
        /// A PropertyForm under a real Canvas. The Canvas ancestor is what keeps the TMP rows
        /// from throwing as they initialise — the EditMode gotcha every UI fixture in this
        /// suite pays for once.
        /// </summary>
        private PropertyForm CreateStandaloneForm()
        {
            var canvasGo = new GameObject("DepthAuthoringTestCanvas");
            _sceneObjects.Add(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return PropertyForm.Create(root.transform, "PresetPropsForm");
        }

        private ParticlesRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();

            var go = new GameObject("TestParticlesEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();

            // Mirrors ParticlesEditorLifecycleTests / ParticlesPresetAutosaveTests so all three
            // fixtures build the editor the same way.
            InvokeMethod(editor, "OnSingletonAwake");
            StubPreviewService(editor);
            return editor;
        }

        /// <summary>
        /// Copied from <see cref="ParticlesEditorLifecycleTests"/> so the fixtures stay in
        /// lockstep: marks the preview service initialised and fills its pool with empty slots,
        /// so nothing in EditMode reaches for a Camera or a RenderTexture.
        /// </summary>
        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            var service = serviceField?.GetValue(editor);
            if (service == null) return;

            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;

            serviceType.GetField("_initialized", bf)?.SetValue(service, true);

            var pool = serviceType.GetField("_pool", bf)?.GetValue(service) as Array;
            if (pool == null) return;
            var thumbSlotType = serviceType.GetNestedType("ThumbSlot", BindingFlags.NonPublic);
            if (thumbSlotType == null) return;
            for (int i = 0; i < pool.Length; i++)
                if (pool.GetValue(i) == null)
                    pool.SetValue(Activator.CreateInstance(thumbSlotType), i);
        }

        // ═══════════════════════════════════════════════════ form / dropdown readers

        /// <summary>
        /// The form's key → widget map. Private by design (the form owns its rows), so the only
        /// way to ask "does a row for this key exist" is through it.
        /// </summary>
        private static IDictionary FormFields(PropertyForm form)
        {
            var f = typeof(PropertyForm).GetField("_fields",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "PropertyForm._fields is gone — this fixture reads the form's " +
                                "row keys through it.");
            var dict = f.GetValue(form) as IDictionary;
            Assert.IsNotNull(dict, "PropertyForm._fields must be a dictionary keyed by row key.");
            return dict;
        }

        private static List<string> FormFieldKeys(PropertyForm form)
        {
            var keys = new List<string>();
            foreach (var k in FormFields(form).Keys) keys.Add((string)k);
            return keys;
        }

        private static Component FormField(PropertyForm form, string key)
        {
            var fields = FormFields(form);
            Assert.IsTrue(fields.Contains(key),
                $"The Properties form offers no row keyed '{key}'. Rows present: " +
                string.Join(", ", FormFieldKeys(form)));
            return (Component)fields[key];
        }

        private static TMP_Dropdown LayerDropdown(PropertyForm form)
        {
            var dd = FormField(form, KEY_LAYER) as TMP_Dropdown;
            Assert.IsTrue(dd != null, "The Sorting Layer row must be a dropdown.");
            Assert.Greater(dd.options.Count, 0, "The Sorting Layer dropdown offers nothing.");
            return dd;
        }

        /// <summary>
        /// Splits the dropdown's entries into the ones that name a project sorting layer (in
        /// the order offered) and the ones that do not — the second list is where the "unset"
        /// entry lands, whatever it happens to be labelled.
        /// </summary>
        private static void SplitOptions(TMP_Dropdown dd, List<string> layers, List<string> others)
        {
            var names = new List<string>(ProjectLayerNames());
            for (int i = 0; i < dd.options.Count; i++)
            {
                string text = dd.options[i].text;
                if (names.Contains(text)) layers.Add(text);
                else others.Add(text);
            }
        }

        private static int RequireOptionIndex(TMP_Dropdown dd, string label)
        {
            for (int i = 0; i < dd.options.Count; i++)
                if (dd.options[i].text == label) return i;
            Assert.Fail($"The Sorting Layer dropdown offers no entry '{label}'. Offered: " +
                        string.Join(", ", OptionLabels(dd)));
            return -1;
        }

        private static string[] OptionLabels(TMP_Dropdown dd)
        {
            var labels = new string[dd.options.Count];
            for (int i = 0; i < dd.options.Count; i++) labels[i] = dd.options[i].text;
            return labels;
        }

        // ═══════════════════════════════════════════════════════════ writer helpers

        private static string WriteLayerIndex(ParticlePresetDefinition def, int index,
                                              out bool ok, out string error)
        {
            ok = ParticlePresetFieldWriter.TrySetField(def, KEY_LAYER, index, out error);
            return def.vfx.sortingLayer;
        }

        /// <summary>
        /// The invariant no failure path may break: whatever ends up in sortingLayer, the
        /// emitter has to be able to resolve it without falling back. Empty is fine (it means
        /// VFX by design); a real layer name is fine; a number rendered as text, or a name the
        /// project does not define, is a preset that boots into the fallback with a warning and
        /// draws in front of the whole world.
        /// </summary>
        private static void AssertLayerFieldIsResolvable(ParticlePresetDefinition def, string when)
        {
            string stored = def.vfx.sortingLayer;
            if (string.IsNullOrEmpty(stored)) return;

            Assert.GreaterOrEqual(DrawOrderIndex(stored), 0,
                $"sortingLayer holds '{stored}' {when}, which is not a layer this project " +
                "defines. ParticleEmitter would warn once and draw the effect on VFX — in " +
                "front of the player, the walls and the canopy — with nothing on screen " +
                "explaining why.");
        }

        // ═══════════════════════════════════════════════════ sorting-layer helpers

        private static string[] ProjectLayerNames()
        {
            var layers = SortingLayer.layers;
            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++) names[i] = layers[i].name;
            return names;
        }

        /// <summary>
        /// Position in <see cref="SortingLayer.layers"/>, which IS the draw order — later in
        /// the array draws in front. The live list is the authority; SortingConfig's constants
        /// only supply the two names this fixture's regression is about.
        /// </summary>
        private static int DrawOrderIndex(string layerName)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == layerName) return i;
            return -1;
        }

        private static int RequireDrawOrderIndex(string layerName)
        {
            int i = DrawOrderIndex(layerName);
            Assert.GreaterOrEqual(i, 0,
                $"Sorting layer '{layerName}' is not defined in ProjectSettings > Tags and " +
                "Layers. SortingConfig names it, so either the layer was deleted or the " +
                "constant was renamed — and half this game's draw order is built on it.");
            return i;
        }

        /// <summary>The layer immediately in front of <paramref name="reference"/>.</summary>
        private static string FirstLayerAbove(string reference)
        {
            int i = RequireDrawOrderIndex(reference);
            var layers = SortingLayer.layers;
            Assert.Less(i + 1, layers.Length,
                $"'{reference}' is the frontmost sorting layer, so nothing can be authored in " +
                "front of it — the regression this fixture pins would be unfixable.");
            return layers[i + 1].name;
        }

        // ══════════════════════════════════════════════════════ reflection plumbing

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        /// <summary>
        /// The warn-once ledger inside ParticleEmitter.Colors.cs. It is a session-lived static
        /// and Domain Reload is OFF, so a fixture that leaves it dirty changes the behaviour of
        /// every fixture that runs after it.
        /// </summary>
        private static void ClearSortingLayerVerdictCache()
        {
            var f = typeof(ParticleEmitter).GetField(
                "_sortingLayerVerdicts", BindingFlags.NonPublic | BindingFlags.Static);
            (f?.GetValue(null) as IDictionary)?.Clear();
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetFieldValue(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static object InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                t = t.BaseType;
            }
            Assert.IsNotNull(m, $"{obj.GetType().Name}.{methodName} is gone.");
            return m.Invoke(obj, args);
        }

        // ══════════════════════════════════════════════════════════ emitter readers

        private static ParticleSystemRenderer RootRendererOf(ParticleEmitter emitter)
        {
            // EnsureParticleSystem always names the root child "Particles"; composite layers
            // are "Layer_0", "Layer_1", ...
            var t = emitter.transform.Find("Particles");
            Assert.IsTrue(t != null,
                "ApplyPreset must have built the root ParticleSystem child 'Particles'.");
            var r = t.GetComponent<ParticleSystemRenderer>();
            Assert.IsTrue(r != null, "The root ParticleSystem must carry a renderer.");
            return r;
        }
    }
}
