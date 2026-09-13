using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// The run's record of killed placements, and the placement file's respawn field. Pure, so every
    /// rule is pinned without a scene.
    /// </summary>
    [TestFixture]
    public class PlacedEntityRunStateTests
    {
        [Test]
        public void ZeroRespawn_StaysDeadForTheRun()
        {
            var s = new PlacedEntityRunState();
            s.MarkDefeated("a", 0f, 1000d);
            Assert.IsTrue(s.IsDefeated("a", 1000d));
            Assert.IsTrue(s.IsDefeated("a", double.MaxValue));

            var due = new List<string>();
            s.CollectDue(double.MaxValue, due);
            Assert.IsEmpty(due, "a never-respawn record is never due");
        }

        [Test]
        public void PositiveRespawn_IsDefeatedUntilItsDeadline()
        {
            var s = new PlacedEntityRunState();
            s.MarkDefeated("a", 30f, 1000d);
            Assert.IsTrue(s.IsDefeated("a", 1029d));
            Assert.IsFalse(s.IsDefeated("a", 1030d));

            var due = new List<string>();
            s.CollectDue(1029d, due);
            Assert.IsEmpty(due);
            s.CollectDue(1030d, due);
            CollectionAssert.AreEqual(new[] { "a" }, due);
        }

        [Test]
        public void Serialize_RoundTrips_AndIsStableAndLocaleIndependent()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");

                var s = new PlacedEntityRunState();
                s.MarkDefeated("zzz", 0f, 5d);
                s.MarkDefeated("aaa", 12.5f, 1789338970.25d);

                string raw = s.Serialize();
                StringAssert.StartsWith("aaa=", raw, "sorted by id, so one run always writes one string");
                StringAssert.DoesNotContain(",", raw, "invariant culture: no decimal comma");

                var back = PlacedEntityRunState.Parse(raw);
                Assert.AreEqual(raw, back.Serialize());
                Assert.IsTrue(back.TryGetRespawnAt("aaa", out double at));
                Assert.AreEqual(1789338982.75d, at, 1e-6);
                Assert.IsTrue(back.TryGetRespawnAt("zzz", out double never));
                Assert.AreEqual(0d, never);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Parse_DropsMalformedEntries_AndKeepsTheRest()
        {
            var s = PlacedEntityRunState.Parse("good=0;;=5;bad=abc;neg=-4;also=NaN;ok=10");
            Assert.IsTrue(s.Contains("good"));
            Assert.IsTrue(s.Contains("ok"));
            Assert.IsTrue(s.Contains("neg"), "a negative deadline reads as never-respawn, not as garbage");
            Assert.IsTrue(s.IsDefeated("neg", double.MaxValue));
            Assert.IsFalse(s.Contains("bad"));
            Assert.IsFalse(s.Contains("also"));
            Assert.AreEqual(3, s.Count);

            Assert.AreEqual(0, PlacedEntityRunState.Parse(null).Count);
            Assert.AreEqual(0, PlacedEntityRunState.Parse("").Count);
        }

        [Test]
        public void CopyFrom_KeepsDeadlinesVerbatim()
        {
            var source = PlacedEntityRunState.Parse("a=12345.5");
            var target = new PlacedEntityRunState();
            target.MarkDefeated("old", 0f, 0d);

            target.CopyFrom(source);

            Assert.IsFalse(target.Contains("old"));
            Assert.IsTrue(target.TryGetRespawnAt("a", out double at));
            Assert.AreEqual(12345.5d, at, 1e-9, "a restored value already IS a deadline; it must not be re-added to now");
        }

        // ── The file's respawn field ─────────────────────────────────────────────

        [Test]
        public void RespawnSeconds_RoundTripsThroughTheFile_AndZeroIsNotWritten()
        {
            var zones = new Dictionary<string, Vector2> { { "Lobby", new Vector2(150f, 50f) } };
            var dies = EntityInstanceSerializer.FromWorldPosition("a", "barbol", "Lobby", new Vector2(160f, 70f), zones["Lobby"], 50);
            var returns = EntityInstanceSerializer.FromWorldPosition("b", "barbol", "Lobby", new Vector2(161f, 70f), zones["Lobby"], 50);
            returns.RespawnSeconds = 45.5f;

            string json = EntityInstanceSerializer.Serialize(new[] { dies, returns });
            Assert.AreEqual(1, CountOccurrences(json, "respawn_seconds"), "0 keeps the record identical to schema v1");

            var back = EntityInstanceSerializer.Deserialize(json, zones, 50);
            Assert.AreEqual(0f, back[0].RespawnSeconds);
            Assert.AreEqual(45.5f, back[1].RespawnSeconds, 0.001f);
        }

        [Test]
        public void ASchemaV1File_ReadsAsStayDead()
        {
            const string v1 = "{\"version\":1,\"instances\":[{\"id\":\"x\",\"monster_key\":\"barbol\",\"zone\":\"Lobby\",\"tile\":[3,4]}]}";
            var back = EntityInstanceSerializer.Deserialize(v1, new Dictionary<string, Vector2>(), 50);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(0f, back[0].RespawnSeconds);
        }

        [Test]
        public void ANegativeOrGarbageRespawn_ReadsAsZero()
        {
            const string json = "{\"version\":2,\"instances\":[" +
                "{\"id\":\"n\",\"monster_key\":\"barbol\",\"zone\":\"Lobby\",\"tile\":[1,1],\"respawn_seconds\":-3}," +
                "{\"id\":\"g\",\"monster_key\":\"barbol\",\"zone\":\"Lobby\",\"tile\":[1,1],\"respawn_seconds\":\"soon\"}]}";
            var back = EntityInstanceSerializer.Deserialize(json, new Dictionary<string, Vector2>(), 50);
            Assert.AreEqual(2, back.Count, "a bad respawn value must not cost the placement");
            Assert.AreEqual(0f, back[0].RespawnSeconds);
            Assert.AreEqual(0f, back[1].RespawnSeconds);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
