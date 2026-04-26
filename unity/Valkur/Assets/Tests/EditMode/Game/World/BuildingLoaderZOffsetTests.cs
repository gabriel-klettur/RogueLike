using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Tests for the <c>z_bottom</c> / <c>z_top</c> overrides parsed by
    /// <see cref="BuildingLoader"/>'s private <c>ParseInstances</c> method.
    ///
    /// These overrides drive the per-instance Z sorting fix introduced together
    /// with the Buildings Editor properties panel (Gap 7). They live inside the
    /// optional <c>"overrides"</c> JSON block, alongside <c>scale</c>,
    /// <c>split_ratio</c> and <c>collider_scope</c>.
    ///
    /// Reference path:
    ///   StreamingAssets/Buildings/buildings_instances.json
    ///   {
    ///     "id": 1, "template_id": 7, "zone": "lobby", "rel_x": 0, "rel_y": 0,
    ///     "overrides": { "z_bottom": -3, "z_top": 5 }
    ///   }
    /// </summary>
    [TestFixture]
    public class BuildingLoaderZOffsetTests
    {
        // ── reflection plumbing (same pattern as BuildingLoaderTests) ─────────────

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
            Assert.IsNotNull(fi, $"Field '{fieldName}' not found on {obj.GetType().Name}.");
            return (T) fi.GetValue(obj);
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        // ──────────────────────────────────────────────────────────────────────────
        // 1. Default values when no overrides block is present
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void NoOverrides_ZBottomOffset_DefaultsToZero()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0}]";
            var items = InvokeParse(json);
            Assert.AreEqual(0, GetField<int>(items[0], "ZBottomOffset"),
                "ZBottomOffset must default to 0 when no 'z_bottom' override is present.");
        }

        [Test]
        public void NoOverrides_ZTopOffset_DefaultsToZero()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0}]";
            var items = InvokeParse(json);
            Assert.AreEqual(0, GetField<int>(items[0], "ZTopOffset"),
                "ZTopOffset must default to 0 when no 'z_top' override is present.");
        }

        [Test]
        public void EmptyOverridesBlock_ZOffsets_DefaultToZero()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,""overrides"":{}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(0, GetField<int>(items[0], "ZBottomOffset"));
            Assert.AreEqual(0, GetField<int>(items[0], "ZTopOffset"));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 2. Positive values
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void ZBottomOffset_PositiveValue_Parsed()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                    ""overrides"":{""z_bottom"":4}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(4, GetField<int>(items[0], "ZBottomOffset"),
                "Positive z_bottom must round-trip into ZBottomOffset.");
        }

        [Test]
        public void ZTopOffset_PositiveValue_Parsed()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                    ""overrides"":{""z_top"":11}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(11, GetField<int>(items[0], "ZTopOffset"),
                "Positive z_top must round-trip into ZTopOffset.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 3. Negative values (used to push a building behind others)
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void ZBottomOffset_NegativeValue_Parsed()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                    ""overrides"":{""z_bottom"":-3}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(-3, GetField<int>(items[0], "ZBottomOffset"),
                "Negative z_bottom must be preserved (used to render below other walls).");
        }

        [Test]
        public void ZTopOffset_NegativeValue_Parsed()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                    ""overrides"":{""z_top"":-7}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(-7, GetField<int>(items[0], "ZTopOffset"),
                "Negative z_top must be preserved.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 4. Both at once + coexistence with other overrides
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void ZBottomAndTop_BothPresent_Parsed()
        {
            const string json =
                @"[{""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                    ""overrides"":{""z_bottom"":-2,""z_top"":3}}]";
            var items = InvokeParse(json);
            Assert.AreEqual(-2, GetField<int>(items[0], "ZBottomOffset"));
            Assert.AreEqual( 3, GetField<int>(items[0], "ZTopOffset"));
        }

        [Test]
        public void ZOffsets_Coexist_WithSplitAndScaleOverrides()
        {
            // Real-world saved entry from the Buildings Editor when an instance
            // has Split + Scale + Z customization at the same time.
            const string json =
                @"[{""id"":99,""template_id"":4,""zone"":""lobby"",""rel_x"":120,""rel_y"":-32,
                    ""overrides"":{
                        ""scale"":[64,96],
                        ""split_ratio"":0.4500,
                        ""collider_scope"":""CU"",
                        ""z_bottom"":2,
                        ""z_top"":-1
                    }}]";
            var items = InvokeParse(json);
            Assert.AreEqual(1, items.Count);
            var dto = items[0];

            Assert.AreEqual(99,        GetField<int>(dto, "Id"));
            Assert.AreEqual(4,         GetField<int>(dto, "TemplateId"));
            Assert.AreEqual(120,       GetField<int>(dto, "RelX"));
            Assert.AreEqual(-32,       GetField<int>(dto, "RelY"));
            Assert.AreEqual(0.45f,     GetField<float>(dto, "SplitRatioOverride"), 0.001f);
            Assert.AreEqual("CU",      GetField<string>(dto, "ColliderScopeOverride"));
            Assert.AreEqual( 2,        GetField<int>(dto, "ZBottomOffset"));
            Assert.AreEqual(-1,        GetField<int>(dto, "ZTopOffset"));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 5. Per-entry isolation
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void ZOffsets_DoNotLeak_BetweenEntries()
        {
            const string json =
                @"[
                    {""id"":1,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0,
                     ""overrides"":{""z_bottom"":5,""z_top"":-2}},
                    {""id"":2,""template_id"":1,""zone"":""lobby"",""rel_x"":0,""rel_y"":0}
                  ]";
            var items = InvokeParse(json);
            Assert.AreEqual(2, items.Count);

            Assert.AreEqual( 5, GetField<int>(items[0], "ZBottomOffset"));
            Assert.AreEqual(-2, GetField<int>(items[0], "ZTopOffset"));

            Assert.AreEqual(0, GetField<int>(items[1], "ZBottomOffset"),
                "Second entry has no override — must default to 0, not inherit from previous entry.");
            Assert.AreEqual(0, GetField<int>(items[1], "ZTopOffset"),
                "Second entry has no override — must default to 0, not inherit from previous entry.");
        }
    }
}
