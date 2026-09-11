using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// A building's Z IS a tile layer: each half names the layer it sits directly above, and
    /// <see cref="SortingConfig.PropSortingLayer"/> answers with a slot that lives between that
    /// layer's tiles and the next layer's. So "does this building draw over that wall" is
    /// decided by sorting-LAYER comparison, by name — never by <c>sortingOrder</c> arithmetic,
    /// which is what the previous Z was and why no value of it could clear a wall painted on
    /// layer 7.
    ///
    /// <para>What is pinned here is the LADDER, as comparisons between live
    /// <see cref="SortingLayer"/> values rather than as literals: re-ordering the list in
    /// TagManager cannot satisfy these while breaking the render. The missing-name case is
    /// made loud on purpose, because <see cref="SortingLayer.NameToID"/> answers 0 (Default)
    /// for a name TagManager does not carry, which puts the renderer behind the whole world
    /// with nothing logged — the way a lost TagManager edit fails.</para>
    /// </summary>
    [TestFixture]
    public class BuildingZLayerTests
    {
        private static int Value(string sortingLayerName)
        {
            bool known = sortingLayerName == "Default" || SortingLayer.NameToID(sortingLayerName) != 0;
            Assert.IsTrue(known, "SortingLayer is missing from TagManager: " + sortingLayerName);
            return SortingLayer.GetLayerValueFromName(sortingLayerName);
        }

        /// <summary>
        /// The tile slot each visual layer paints into, asked of the same resolver the tilemap
        /// builder uses. It used to be a literal array mirroring that switch, which is one more
        /// copy to forget when the ladder grows.
        /// </summary>
        private static string TileSlotFor(int visualLayer) => SortingConfig.TileSortingLayer(visualLayer);

        private static string NextRenderedTileSlot(int z)
        {
            for (int n = z + 1; n <= SortingConfig.MAX_VISUAL_LAYER; n++)
                if (TileSlotFor(n) != null) return TileSlotFor(n);
            return null;
        }

        // ── The case that started this ───────────────────────────────────────

        [Test]
        public void ACanopyWithZSeven_DrawsOverATilePaintedOnLayerSeven()
        {
            Assert.Greater(Value(SortingConfig.PropSortingLayer(7)), Value(SortingConfig.LAYER_OBJECTS_HIGH),
                "Z 7 must put a building half over a tile painted on layer 7. This was structurally " +
                "impossible while every building lived on WallsTop.");
        }

        // ── The ladder ───────────────────────────────────────────────────────

        [Test]
        public void EveryZ_SitsAboveItsOwnTiles_AndBelowTheNextLayersTiles()
        {
            for (int z = 0; z <= SortingConfig.MAX_VISUAL_LAYER; z++)
            {
                int slot = Value(SortingConfig.PropSortingLayer(z));

                if (TileSlotFor(z) != null)
                    Assert.Greater(slot, Value(TileSlotFor(z)),
                        "Z " + z + " must draw over the tiles of layer " + z + ".");

                string next = NextRenderedTileSlot(z);
                if (next != null)
                    Assert.Less(slot, Value(next),
                        "Z " + z + " must stay under the tiles of the next rendered layer (" + next + "), " +
                        "or the ladder stops meaning anything above it.");
            }
        }

        [Test]
        public void EveryZ_HasItsOwnSlot_InAscendingOrder()
        {
            for (int z = 1; z <= SortingConfig.MAX_VISUAL_LAYER; z++)
                Assert.Greater(Value(SortingConfig.PropSortingLayer(z)), Value(SortingConfig.PropSortingLayer(z - 1)),
                    "Z " + z + " must be a strictly higher slot than Z " + (z - 1) + ".");
        }

        [Test]
        public void TheDefaultZ_KeepsTheSandwichAroundTheEntitySlot()
        {
            // The split ratio only means something if the player can stand between the two
            // halves: footprint below the entity slot, canopy above it.
            int footprint = Value(SortingConfig.PropSortingLayer(SortingConfig.DEFAULT_PROP_Z_BOTTOM));
            int entities  = Value(SortingConfig.LAYER_ENTITIES);
            int canopy    = Value(SortingConfig.PropSortingLayer(SortingConfig.DEFAULT_PROP_Z_TOP));

            Assert.Less(footprint, entities, "The default footprint must draw under the player.");
            Assert.Greater(canopy, entities, "The default canopy must draw over the player.");
        }

        [Test]
        public void TheTopSlot_StaysBelowTheEntitySlotOfLayerEight()
        {
            // VisualLayerSortingSync puts an entity on visual layer 8 onto EntitiesOverhead.
            // A player standing ON a Z-8 building has to be visible on top of it.
            Assert.Less(Value(SortingConfig.PropSortingLayer(8)), Value(SortingConfig.LAYER_ENTITIES_OVERHEAD));
        }

        // ── The resolver ─────────────────────────────────────────────────────

        [Test]
        public void PropSortingLayer_ClampsToTheEnds_AndNeverAnswersNull()
        {
            Assert.AreEqual(SortingConfig.PropSortingLayer(0), SortingConfig.PropSortingLayer(-5),
                "An out-of-range Z lands at the nearest end. Null or Default would put the building behind the world.");
            Assert.AreEqual(SortingConfig.PropSortingLayer(SortingConfig.MAX_VISUAL_LAYER), SortingConfig.PropSortingLayer(99));
            for (int z = 0; z <= SortingConfig.MAX_VISUAL_LAYER; z++)
                Assert.IsNotNull(SortingConfig.PropSortingLayer(z));
        }
    }
}
