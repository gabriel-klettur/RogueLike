using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The Z halves of a building record — <c>layer_bottom</c> / <c>layer_top</c> — as parsed
    /// by <see cref="BuildingLoader"/>'s private <c>ParseInstances</c>.
    ///
    /// <para>Z is a tile-layer index (0..8): the layer that half sits directly above. It
    /// replaced <c>z_bottom</c> / <c>z_top</c>, which held a signed TIER whose only effect on
    /// the sorting layer was its sign. The shipped world still carries four rows in the old
    /// keys, so the parser reads both, prefers the new one, and maps the old one through the
    /// exact rule the old code applied — which is what lets those four come back at the depth
    /// they were authored at instead of at the default.</para>
    /// </summary>
    [TestFixture]
    public class BuildingLoaderZOffsetTests
    {
        private static MethodInfo GetParseMethod()
        {
            var m = typeof(BuildingLoader).GetMethod(
                "ParseInstances",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "BuildingLoader.ParseInstances not found via reflection.");
            return m;
        }

        private static IList InvokeParse(string json)
            => GetParseMethod().Invoke(null, new object[] { json }) as IList;

        private static T GetField<T>(object obj, string fieldName)
        {
            var fi = obj.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "Field not found on " + obj.GetType().Name + ": " + fieldName);
            return (T) fi.GetValue(obj);
        }

        private static string Row(string overrides)
            => "[{\"id\":1,\"template_id\":1,\"zone\":\"lobby\",\"rel_x\":0,\"rel_y\":0"
             + (overrides == null ? "" : ",\"overrides\":{" + overrides + "}") + "}]";

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        // ── Defaults ─────────────────────────────────────────────────────────

        [Test]
        public void NoOverrides_ZDefaultsToTheSandwich()
        {
            var items = InvokeParse(Row(null));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM, GetField<int>(items[0], "ZBottom"),
                "A row with no overrides must come back at the default footprint layer, not 0 — " +
                "0 is a real layer (above Ground) and would drop every plain building under the floor decals.");
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP, GetField<int>(items[0], "ZTop"));
        }

        [Test]
        public void EmptyOverridesBlock_ZDefaultsToTheSandwich()
        {
            var items = InvokeParse(Row(""));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM, GetField<int>(items[0], "ZBottom"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP, GetField<int>(items[0], "ZTop"));
        }

        // ── The current keys ─────────────────────────────────────────────────

        [Test]
        public void LayerBottom_AndLayerTop_AreReadVerbatim()
        {
            var items = InvokeParse(Row("\"layer_bottom\":3,\"layer_top\":7"));
            Assert.AreEqual(3, GetField<int>(items[0], "ZBottom"));
            Assert.AreEqual(7, GetField<int>(items[0], "ZTop"));
        }

        [Test]
        public void EitherHalf_MayBeAuthoredAlone()
        {
            var bottomOnly = InvokeParse(Row("\"layer_bottom\":2"));
            Assert.AreEqual(2, GetField<int>(bottomOnly[0], "ZBottom"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP, GetField<int>(bottomOnly[0], "ZTop"));

            var topOnly = InvokeParse(Row("\"layer_top\":8"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM, GetField<int>(topOnly[0], "ZBottom"));
            Assert.AreEqual(8, GetField<int>(topOnly[0], "ZTop"));
        }

        // ── The legacy keys ──────────────────────────────────────────────────

        [Test]
        public void LegacyPositiveZBottom_LandsWhereThePromotionPutIt()
        {
            // Old rule: z_bottom > 0 promoted the footprint from WallsBottom to WallsTop.
            var items = InvokeParse(Row("\"z_bottom\":10"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP, GetField<int>(items[0], "ZBottom"),
                "A footprint the old code promoted onto WallsTop must come back above layer 6, " +
                "or the four shipped rows that use it drop a layer on the first load after the change.");
        }

        [Test]
        public void LegacyNonPositiveZBottom_StaysOnTheDefault()
        {
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM,
                GetField<int>(InvokeParse(Row("\"z_bottom\":0"))[0], "ZBottom"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM,
                GetField<int>(InvokeParse(Row("\"z_bottom\":-3"))[0], "ZBottom"),
                "A negative z_bottom never moved the footprint off WallsBottom; it only lowered its order.");
        }

        [Test]
        public void LegacyNegativeZTop_LandsWhereTheDemotionPutIt()
        {
            // Old rule: z_top < 0 demoted the canopy from WallsTop to WallsBottom.
            var items = InvokeParse(Row("\"z_top\":-7"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM, GetField<int>(items[0], "ZTop"));
        }

        [Test]
        public void LegacyNonNegativeZTop_StaysOnTheDefault()
        {
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP,
                GetField<int>(InvokeParse(Row("\"z_top\":0"))[0], "ZTop"));
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP,
                GetField<int>(InvokeParse(Row("\"z_top\":12"))[0], "ZTop"),
                "A positive z_top never moved the canopy off WallsTop; the magnitude only ordered it against other canopies.");
        }

        [Test]
        public void TheLegacyMapping_IsTheOneTheLoaderExposes()
        {
            // The parser and the public helpers must be the same rule, or a caller migrating a
            // record by hand lands somewhere the loader would not have.
            Assert.AreEqual(BuildingLoader.LegacyZBottomToLayer(5),
                GetField<int>(InvokeParse(Row("\"z_bottom\":5"))[0], "ZBottom"));
            Assert.AreEqual(BuildingLoader.LegacyZTopToLayer(-1),
                GetField<int>(InvokeParse(Row("\"z_top\":-1"))[0], "ZTop"));
        }

        [Test]
        public void TheCurrentKey_WinsOverTheLegacyOne_WhenBothArePresent()
        {
            // A row the editor has already rewritten may still carry the old key if it was
            // hand-edited. The new key is the authored intent.
            var items = InvokeParse(Row("\"layer_bottom\":2,\"z_bottom\":10,\"layer_top\":8,\"z_top\":-5"));
            Assert.AreEqual(2, GetField<int>(items[0], "ZBottom"));
            Assert.AreEqual(8, GetField<int>(items[0], "ZTop"));
        }

        // ── Coexistence and isolation ────────────────────────────────────────

        [Test]
        public void Z_Coexists_WithSplitScaleAndScope()
        {
            const string json =
                "[{\"id\":99,\"template_id\":4,\"zone\":\"lobby\",\"rel_x\":120,\"rel_y\":-32," +
                "\"overrides\":{\"scale\":[64,96],\"split_ratio\":0.4500,\"collider_scope\":\"CU\"," +
                "\"layer_bottom\":5,\"layer_top\":7}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);
            var dto = items[0];

            Assert.AreEqual(99,    GetField<int>(dto, "Id"));
            Assert.AreEqual(4,     GetField<int>(dto, "TemplateId"));
            Assert.AreEqual(120,   GetField<int>(dto, "RelX"));
            Assert.AreEqual(-32,   GetField<int>(dto, "RelY"));
            Assert.AreEqual(0.45f, GetField<float>(dto, "SplitRatioOverride"), 0.001f);
            Assert.AreEqual("CU",  GetField<string>(dto, "ColliderScopeOverride"));
            Assert.AreEqual(5,     GetField<int>(dto, "ZBottom"));
            Assert.AreEqual(7,     GetField<int>(dto, "ZTop"));
        }

        [Test]
        public void Z_DoesNotLeak_BetweenEntries()
        {
            const string json =
                "[{\"id\":1,\"template_id\":1,\"zone\":\"lobby\",\"rel_x\":0,\"rel_y\":0," +
                "\"overrides\":{\"layer_bottom\":1,\"layer_top\":8}}," +
                "{\"id\":2,\"template_id\":1,\"zone\":\"lobby\",\"rel_x\":0,\"rel_y\":0}]";
            var items = InvokeParse(json);
            Assert.AreEqual(2, items.Count);

            Assert.AreEqual(1, GetField<int>(items[0], "ZBottom"));
            Assert.AreEqual(8, GetField<int>(items[0], "ZTop"));

            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_BOTTOM, GetField<int>(items[1], "ZBottom"),
                "Second entry has no override — must take the default, not inherit from the previous entry.");
            Assert.AreEqual(SortingConfig.DEFAULT_PROP_Z_TOP, GetField<int>(items[1], "ZTop"));
        }
    }
}
