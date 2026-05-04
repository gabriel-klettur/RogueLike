using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.WorldDrops;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// Pure data-shape coverage for <see cref="ItemDropInstance"/> and the
    /// <see cref="ItemDropsFile"/> wrapper. Catches schema regressions early
    /// (renamed fields, lost defaults, JsonUtility incompatibilities).
    /// </summary>
    [TestFixture]
    public class ItemDropInstanceTests
    {
        [Test]
        public void NewDropId_ReturnsUniqueNonEmptyValues()
        {
            string a = ItemDropInstance.NewDropId();
            string b = ItemDropInstance.NewDropId();
            Assert.IsFalse(string.IsNullOrEmpty(a));
            Assert.IsFalse(string.IsNullOrEmpty(b));
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Constructor_DefensiveDefaults()
        {
            var inst = new ItemDropInstance(
                dropId: "id1", itemId: "iron_sword", quantity: 3,
                position: new Vector2(2.5f, -4f),
                zoneId: null, zLayer: 0, createdAtUnixMs: 1234L,
                despawnTtlSeconds: -10f, source: ItemDropSource.Editor);

            Assert.AreEqual("",  inst.zoneId);                     // null → empty string
            Assert.AreEqual(0f,  inst.despawnTtlSeconds);          // negatives clamped to 0
            Assert.IsTrue(inst.IsInfinite);                        // 0 ⇒ infinite
            Assert.AreEqual(ItemDropSource.Editor, inst.Source);
        }

        [Test]
        public void IsInfinite_TrueOnlyForZeroOrLess()
        {
            Assert.IsTrue(new ItemDropInstance { despawnTtlSeconds = 0f  }.IsInfinite);
            Assert.IsTrue(new ItemDropInstance { despawnTtlSeconds = -1f }.IsInfinite);
            Assert.IsFalse(new ItemDropInstance { despawnTtlSeconds = 0.0001f }.IsInfinite);
            Assert.IsFalse(new ItemDropInstance { despawnTtlSeconds = 60f     }.IsInfinite);
        }

        [Test]
        public void Source_BackedByRawInt_RoundTripsViaJson()
        {
            var inst = new ItemDropInstance
            {
                dropId = "d", itemId = "i", quantity = 1,
                position = new Vector2(1f, 2f),
                Source = ItemDropSource.Loot,
            };
            string json = JsonUtility.ToJson(inst);
            var parsed = JsonUtility.FromJson<ItemDropInstance>(json);
            Assert.AreEqual(ItemDropSource.Loot, parsed.Source);
            Assert.AreEqual((int)ItemDropSource.Loot, parsed.sourceRaw);
        }

        [Test]
        public void Clone_IsDeep_NotAlias()
        {
            var a = new ItemDropInstance(
                "id", "fireball_scroll", 5, new Vector2(7f, 9f),
                "void_zone", 3, 1700000000000L, 120f, ItemDropSource.Quest);

            var b = a.Clone();
            Assert.AreNotSame(a, b);
            Assert.AreEqual(a.dropId,    b.dropId);
            Assert.AreEqual(a.itemId,    b.itemId);
            Assert.AreEqual(a.quantity,  b.quantity);
            Assert.AreEqual(a.position,  b.position);
            Assert.AreEqual(a.zoneId,    b.zoneId);
            Assert.AreEqual(a.zLayer,    b.zLayer);
            Assert.AreEqual(a.createdAtUnixMs,   b.createdAtUnixMs);
            Assert.AreEqual(a.despawnTtlSeconds, b.despawnTtlSeconds);
            Assert.AreEqual(a.Source,    b.Source);

            // Mutate original; clone must remain untouched.
            a.quantity = 99;
            Assert.AreEqual(5, b.quantity);
        }

        [Test]
        public void ItemDropsFile_RoundTripsViaJsonUtility()
        {
            var file = new ItemDropsFile
            {
                schemaVersion = ItemDropsFile.CurrentSchemaVersion,
                drops = new[]
                {
                    new ItemDropInstance("a", "gold", 100, new Vector2(0,0),
                        "starter_zone", 0, 1L, 0f, ItemDropSource.Editor),
                    new ItemDropInstance("b", "health_potion", 1, new Vector2(3.5f, 2f),
                        "cave", 1, 2L, 60f, ItemDropSource.Loot),
                },
            };
            string json   = JsonUtility.ToJson(file);
            var   parsed = JsonUtility.FromJson<ItemDropsFile>(json);

            Assert.AreEqual(ItemDropsFile.CurrentSchemaVersion, parsed.schemaVersion);
            Assert.AreEqual(2, parsed.drops.Length);
            Assert.AreEqual("gold",          parsed.drops[0].itemId);
            Assert.AreEqual(100,             parsed.drops[0].quantity);
            Assert.IsTrue(parsed.drops[0].IsInfinite);
            Assert.AreEqual("health_potion", parsed.drops[1].itemId);
            Assert.AreEqual(60f,             parsed.drops[1].despawnTtlSeconds);
            Assert.AreEqual(ItemDropSource.Loot, parsed.drops[1].Source);
        }
    }
}
