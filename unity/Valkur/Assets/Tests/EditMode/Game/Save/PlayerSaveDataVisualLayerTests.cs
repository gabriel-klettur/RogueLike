using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Round-trip + legacy migration guard for the <see cref="PlayerSaveData.visualLayer"/>
    /// field (M1.7 of the per-visual-layer collisions feature).
    ///
    /// Why this matters: M2 will hang Physics2D collider filtering off this value, and
    /// the field is meant to persist transparently — a regression that drops it on
    /// serialize or fails to default it to 0 on legacy loads would silently break
    /// player-layer-aware gameplay after the M2 ship.
    /// </summary>
    [TestFixture]
    public class PlayerSaveDataVisualLayerTests
    {
        [Test]
        public void Default_IsZero()
        {
            var psd = new PlayerSaveData();
            Assert.AreEqual(0, psd.visualLayer,
                "New PlayerSaveData must default visualLayer to 0 (Ground) so legacy " +
                "code paths that don't touch the field don't change semantics.");
        }

        [Test]
        public void RoundTrip_PreservesAuthoredValue()
        {
            var original = new PlayerSaveData
            {
                playerClass = "warrior",
                position = new Vector2(12.5f, -3.25f),
                currentZone = "zone_a",
                hp = 80, maxHp = 100,
                mana = 30f, maxMana = 50f,
                experience = 1234, level = 5,
                visualLayer = 4,
            };

            string json = JsonUtility.ToJson(original);
            StringAssert.Contains("\"visualLayer\":4", json,
                "Serialized JSON must carry the visualLayer field explicitly.");

            var restored = JsonUtility.FromJson<PlayerSaveData>(json);
            Assert.AreEqual(4, restored.visualLayer);
            Assert.AreEqual(original.level,       restored.level);
            Assert.AreEqual(original.currentZone, restored.currentZone);
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(8)]
        public void RoundTrip_AllValidLayerIndices(int layer)
        {
            var psd = new PlayerSaveData { visualLayer = layer };
            string json = JsonUtility.ToJson(psd);
            var restored = JsonUtility.FromJson<PlayerSaveData>(json);
            Assert.AreEqual(layer, restored.visualLayer);
        }

        /// <summary>
        /// THE migration guard. A pre-feature JSON (no visualLayer field at all) must
        /// deserialize cleanly with visualLayer = 0 — preserving the pre-M1.7
        /// behaviour where the player always loaded on Ground. Failing here would
        /// silently break every legacy save the day M2 ships.
        /// </summary>
        [Test]
        public void LegacyJsonWithoutField_LoadsAsZero()
        {
            const string legacyJson =
                "{" +
                "\"playerClass\":\"warrior\"," +
                "\"position\":{\"x\":1.0,\"y\":2.0}," +
                "\"currentZone\":\"zone_legacy\"," +
                "\"hp\":50,\"maxHp\":50," +
                "\"mana\":10.0,\"maxMana\":10.0," +
                "\"experience\":0,\"level\":1" +
                "}";

            var psd = JsonUtility.FromJson<PlayerSaveData>(legacyJson);
            Assert.IsNotNull(psd);
            Assert.AreEqual(0, psd.visualLayer,
                "Legacy JSON without the field must deserialize visualLayer as 0 — " +
                "JsonUtility tolerates missing fields and we rely on that to skip a " +
                "schema bump.");
            Assert.AreEqual("zone_legacy", psd.currentZone,
                "Other fields must still hydrate correctly — sanity check.");
        }

        /// <summary>
        /// JsonUtility uses default(int) = 0 for missing fields. Pin that we don't
        /// somehow read garbage. Belt-and-suspenders for a single-line invariant
        /// because the entire migration story depends on it.
        /// </summary>
        [Test]
        public void EmptyJson_VisualLayerStillZero()
        {
            var psd = JsonUtility.FromJson<PlayerSaveData>("{}");
            Assert.AreEqual(0, psd.visualLayer);
        }
    }
}
