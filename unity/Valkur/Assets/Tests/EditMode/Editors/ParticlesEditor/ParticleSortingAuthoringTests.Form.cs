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
    /// <summary>ParticleSortingAuthoringTests: the form tests. SetUp, TearDown and helpers live in ParticleSortingAuthoringTests.cs.</summary>
    public partial class ParticleSortingAuthoringTests
    {
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
            Assert.AreEqual(authored, SelectedLayer(dd),
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
            Assert.AreEqual(SortingConfig.LAYER_VFX, SelectedLayer(dd),
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
                // The row DISPLAYS "10 · PropsL4" and STORES "PropsL4". The name is read back
                // through the production inverse rather than by splitting on a copy of the
                // separator here, so a change to the label format cannot leave this fixture
                // passing while reporting on a string that no longer means what it thinks.
                string layer = ParticlePresetFieldWriter.LayerNameFromLabel(label);
                probe.vfx.sortingLayer = _priorLayer;
                string stored = WriteLayerIndex(probe, i, out bool ok, out string err);

                Assert.IsTrue(ok, $"Entry {i} ('{label}') was refused by the writer: {err}. " +
                                  "Every entry the panel offers must be selectable.");
                if (layerNames.Contains(layer))
                    Assert.AreEqual(layer, stored,
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
    }
}
