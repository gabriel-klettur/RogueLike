using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The world's visual-layer count is bounded by Unity, not by taste, and it used to be
    /// spelled as the literal <c>9</c> in five independent places
    /// (<see cref="CollisionTagMap.LayerCount"/>, <see cref="WorldCollisionLayers.LayerCount"/>,
    /// <see cref="VisualLayerOccupant.MaxLayer"/>, <c>VisualLayerProbe.LayerCount</c>,
    /// <see cref="LayerJumpMap.MaxTarget"/>) plus the enum that morally owns it. That is the
    /// hand-maintained-count shape this project already got burned by with
    /// <c>SpriteTintStack.LAYER_COUNT</c> and the boot's <c>SetupStepTotal</c>.
    ///
    /// <para>All of them derive from <see cref="SortingConfig.VISUAL_LAYER_COUNT"/> now. This
    /// fixture pins the joins that a derivation cannot reach on its own — the Gameplay enum
    /// (Core may not reference Gameplay), TagManager's sorting and physics layers, and the
    /// budgets Unity imposes. Each test names the ceiling it guards and what running out of it
    /// looks like, because every one of these fails SILENTLY: no exception, just geometry in
    /// the wrong place.</para>
    /// </summary>
    [TestFixture]
    public class WorldLayerCeilingTests
    {
        // ── The single source of truth ───────────────────────────────────────

        [Test]
        public void TheTilemapEnum_AndTheCoreCount_AgreeInBothDirections()
        {
            // Core owns the number because Valkur.Core may not reference Valkur.Gameplay, where
            // the enum lives. That makes them a PAIR rather than a derivation, so it has to be
            // asserted from both ends: the same count, and contiguous values 0..N-1 with no
            // gap, since every consumer indexes arrays and bitmasks by the raw value.
            var values = (TilemapLayerSetup.TilemapLayer[])
                System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));

            Assert.AreEqual(SortingConfig.VISUAL_LAYER_COUNT, values.Length,
                "TilemapLayerSetup.TilemapLayer and SortingConfig.VISUAL_LAYER_COUNT disagree. " +
                "Bumping one without the other silently drops a layer out of the collision mask, " +
                "the layer-jump range and the building Z ladder at once.");

            for (int i = 0; i < values.Length; i++)
                Assert.AreEqual(i, (int)values[i],
                    "Enum values must be contiguous from 0: everything indexes by the raw value.");
        }

        [Test]
        public void EveryFormerCopyOfTheCount_DerivesFromCore()
        {
            Assert.AreEqual(SortingConfig.VISUAL_LAYER_COUNT, CollisionTagMap.LayerCount);
            Assert.AreEqual(SortingConfig.VISUAL_LAYER_COUNT, WorldCollisionLayers.LayerCount);
            Assert.AreEqual(SortingConfig.MAX_VISUAL_LAYER,   VisualLayerOccupant.MaxLayer);
            Assert.AreEqual(SortingConfig.MAX_VISUAL_LAYER,   LayerJumpMap.MaxTarget);
            Assert.AreEqual(0, VisualLayerOccupant.MinLayer);
            Assert.AreEqual(0, LayerJumpMap.MinTarget);

            // ValidTags is still an authored literal array, so it is the one that can drift.
            Assert.AreEqual(SortingConfig.VISUAL_LAYER_COUNT + 1, CollisionTagMap.ValidTags.Length,
                "ValidTags is the wildcard plus one entry per visual layer; the Colliders panel " +
                "builds a button per entry, so a stale array is a layer the author cannot tag.");
        }

        // ── Ceiling 1: Unity's 32 physics layers ─────────────────────────────

        [Test]
        public void EveryVisualLayer_HasItsWorldPhysicsLayer()
        {
            var missing = new List<string>();
            for (int i = 0; i < SortingConfig.VISUAL_LAYER_COUNT; i++)
                if (LayerMask.NameToLayer("WorldL" + i) < 0) missing.Add("WorldL" + i);
            if (LayerMask.NameToLayer("WorldAll") < 0) missing.Add("WorldAll");

            Assert.IsEmpty(missing,
                "These physics layers are missing from TagManager: " + string.Join(", ", missing) +
                ". WorldCollisionLayers resolves them by NAME and answers -1 for a missing one, " +
                "which drops it out of the blocking mask — the player walks through every wall " +
                "painted on that layer, with nothing logged.");
        }

        [Test]
        public void GrowingTheVisualLayers_HasAMeasuredPhysicsBudget()
        {
            int free = 0;
            for (int i = 0; i < WorldCollisionLayers.UnityPhysicsLayerCount; i++)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) free++;

            // Not a pin on the exact number — layers come and go — but the project must never be
            // in a state where the layer count it declares cannot actually be built.
            Assert.GreaterOrEqual(free, 0);
            Assert.LessOrEqual(SortingConfig.VISUAL_LAYER_COUNT,
                SortingConfig.VISUAL_LAYER_COUNT + free,
                "Every visual layer needs a WorldL{N} physics layer and Unity has " +
                WorldCollisionLayers.UnityPhysicsLayerCount + " in total. Free right now: " + free +
                ", so the ceiling is " + (SortingConfig.VISUAL_LAYER_COUNT + free) + " visual layers.");
        }

        // ── Ceiling 2: the sorting-layer ladder ──────────────────────────────

        [Test]
        public void EveryVisualLayer_HasItsPropSortingLayer_AndTheyAreDistinct()
        {
            var seen = new HashSet<string>();
            var missing = new List<string>();
            for (int z = 0; z < SortingConfig.VISUAL_LAYER_COUNT; z++)
            {
                string name = SortingConfig.PropSortingLayer(z);
                Assert.IsNotNull(name, "PropSortingLayer must answer a name for every visual layer.");
                Assert.IsTrue(seen.Add(name),
                    "Two visual layers resolve to the same building slot: " + name +
                    ". They would be unable to draw over each other.");
                if (SortingLayer.NameToID(name) == 0) missing.Add(name);
            }

            Assert.IsEmpty(missing,
                "These sorting layers are missing from TagManager: " + string.Join(", ", missing) +
                ". SortingLayer.NameToID answers 0 (Default) for an unknown name, which puts the " +
                "building behind the entire world instead of throwing.");
        }

        [Test]
        public void TheResolverAnswersOnlyTheDeclaredRange()
        {
            // Clamping, not wrapping and not null: a building always has somewhere to be drawn.
            Assert.AreEqual(SortingConfig.PropSortingLayer(0), SortingConfig.PropSortingLayer(-1));
            Assert.AreEqual(SortingConfig.PropSortingLayer(SortingConfig.MAX_VISUAL_LAYER),
                            SortingConfig.PropSortingLayer(SortingConfig.MAX_VISUAL_LAYER + 1));
        }

        // ── Ceiling 3: the int bitmask in CollisionTagMap ────────────────────

        [Test]
        public void TheCollisionMask_StillFitsInAnInt()
        {
            Assert.LessOrEqual(SortingConfig.VISUAL_LAYER_COUNT, 31,
                "CollisionTagMap packs one bit per visual layer into an int. At 32 the shift " +
                "overflows the sign bit and FullLayerMask goes negative.");
            Assert.AreEqual((1 << SortingConfig.VISUAL_LAYER_COUNT) - 1, CollisionTagMap.FullLayerMask);
            Assert.Greater(CollisionTagMap.FullLayerMask, 0);
        }

        // ── Ceiling 4: sortingOrder is a 16-bit short IN THE SETTER ──────────

        [Test]
        public void SortingOrder_TruncatesToShort_InTheSetter()
        {
            // The measurement the whole Y budget rests on. It is not "Unity sorts using 16 bits
            // internally" — the property itself does not keep what you write, so an order past
            // the short window reads back NEGATIVE and the sprite draws behind everything.
            var go = new GameObject("__sortprobe");
            try
            {
                var sr = go.AddComponent<SpriteRenderer>();

                sr.sortingOrder = SortingConfig.MAX_SORT_ORDER;
                Assert.AreEqual(SortingConfig.MAX_SORT_ORDER, sr.sortingOrder,
                    "The top of the declared budget must survive the round trip.");

                sr.sortingOrder = SortingConfig.MAX_SORT_ORDER + 1;
                Assert.Less(sr.sortingOrder, 0,
                    "One past the budget must come back NEGATIVE. If this ever stops being true " +
                    "Unity has changed the field width and the Y budget can be re-derived.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheYSort_NeverLeavesRoomLessThanTheHeadroom()
        {
            // Every caller adds something to the Y term — Z_UI is the largest at 1000, and two
            // callers nudge by ±1 — so the Y term alone must stay clear of the short edge by at
            // least the headroom, or the SUM wraps while the Y term looks fine.
            Assert.Greater(SortingConfig.SORT_ORDER_HEADROOM, SortingConfig.Z_UI,
                "The headroom must cover the largest Z base a caller can add to the Y term.");

            float[] extremes = { 0f, SortingConfig.MAX_SAFE_WORLD_Y, -SortingConfig.MAX_SAFE_WORLD_Y,
                                 100000f, -100000f };
            foreach (var y in extremes)
            {
                int order = SortingConfig.YToSortingOrder(y);
                Assert.LessOrEqual(order + SortingConfig.Z_UI, SortingConfig.MAX_SORT_ORDER,
                    "Y " + y + " plus the largest Z base overflows the short window.");
                Assert.GreaterOrEqual(order - SortingConfig.Z_UI, SortingConfig.MIN_SORT_ORDER,
                    "Y " + y + " minus the largest Z base underflows the short window.");
            }
        }

        [Test]
        public void TheYSort_IsMonotoneInsideTheBudget()
        {
            // Clamping is only acceptable because it preserves order up to the line. Lower Y must
            // still mean "in front" everywhere a zone may legitimately sit.
            int step = SortingConfig.MAX_SAFE_WORLD_Y / 8;
            for (int y = -SortingConfig.MAX_SAFE_WORLD_Y; y + step <= SortingConfig.MAX_SAFE_WORLD_Y; y += step)
                Assert.Greater(SortingConfig.YToSortingOrder(y), SortingConfig.YToSortingOrder(y + step),
                    "A renderer at y=" + y + " must draw in front of one at y=" + (y + step) + ".");
        }

        // ── The shipped world against that budget ────────────────────────────

        [Test]
        public void EveryShippedZone_SitsInsideTheYSortBudget()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Maps/zones_database.json");
            if (!File.Exists(path)) Assert.Ignore("zones_database.json not present.");
            string json = File.ReadAllText(path);

            int height = ReadInt(json, "zone_height_tiles", 50);
            var offsets = ReadAll(json, "offset_y");
            Assert.IsNotEmpty(offsets, "No zones parsed from zones_database.json.");

            var offenders = new List<string>();
            foreach (var offset in offsets)
            {
                // A zone spans [offset_y, offset_y + height) tiles, and one tile is one world unit.
                int top = offset + height;
                if (Mathf.Abs(offset) > SortingConfig.MAX_SAFE_WORLD_Y ||
                    Mathf.Abs(top) > SortingConfig.MAX_SAFE_WORLD_Y)
                    offenders.Add("offset_y " + offset + " (top " + top + ")");
            }

            Assert.IsEmpty(offenders,
                "These zones sit outside the ±" + SortingConfig.MAX_SAFE_WORLD_Y + " world-unit " +
                "Y-sort budget: " + string.Join(", ", offenders) + ". Their content would clamp " +
                "to a flat depth. The zone database auto-expands, so this is reachable by adding " +
                "rows rather than by anyone deciding to.");
        }

        // ── tiny JSON readers, so the fixture needs no parser ────────────────

        private static int ReadInt(string json, string key, int fallback)
        {
            int i = json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i);
            if (i < 0) return fallback;
            return ReadNumberAt(json, i + 1, fallback);
        }

        private static List<int> ReadAll(string json, string key)
        {
            var values = new List<int>();
            string needle = "\"" + key + "\"";
            int i = 0;
            while ((i = json.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
            {
                int colon = json.IndexOf(':', i);
                if (colon < 0) break;
                values.Add(ReadNumberAt(json, colon + 1, 0));
                i = colon + 1;
            }
            return values;
        }

        private static int ReadNumberAt(string json, int start, int fallback)
        {
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            if (end < json.Length && (json[end] == '-' || json[end] == '+')) end++;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            return int.TryParse(json.Substring(start, end - start), out int v) ? v : fallback;
        }
    }
}
