using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// Pure POCO tests for the SaveTelemetry static ring buffer.
    /// No MonoBehaviour, no SaveService — telemetry logic only.
    /// </summary>
    public class SaveTelemetryTests
    {
        // Capture for cleanup in TearDown
        private Action<SaveTelemetryEntry> _subscribedHandler;

        [SetUp]
        public void SetUp()
        {
            SaveTelemetry.ResetForTests();
            _subscribedHandler = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_subscribedHandler != null)
                SaveTelemetry.OnEntryRecorded -= _subscribedHandler;
            SaveTelemetry.ResetForTests();
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static SaveTelemetryEntry MakeEntry(string reason = "test",
            SaveTelemetryEntry.SaveKind kind = SaveTelemetryEntry.SaveKind.QuickSave,
            bool success = true)
        {
            return new SaveTelemetryEntry(kind, reason, success,
                sizeBytes: 128, durationMs: 10.0, path: "/save.json", wasAsync: false);
        }

        // ─── A ───────────────────────────────────────────────────────────────────

        [Test]
        public void Record_AppendsEntry_FiresEvent()
        {
            // Arrange
            int eventFiredCount = 0;
            SaveTelemetryEntry? capturedEntry = null;

            _subscribedHandler = e =>
            {
                eventFiredCount++;
                capturedEntry = e;
            };
            SaveTelemetry.OnEntryRecorded += _subscribedHandler;

            var entry = MakeEntry("first");

            // Act
            SaveTelemetry.Record(entry);

            // Assert
            var snapshot = SaveTelemetry.Snapshot();
            Assert.AreEqual(1, snapshot.Count, "Snapshot should have exactly 1 entry.");
            Assert.AreEqual("first", snapshot[0].Reason);
            Assert.AreEqual(1, SaveTelemetry.TotalRecorded, "TotalRecorded should be 1.");
            Assert.AreEqual(1, eventFiredCount, "Event should have fired exactly once.");
            Assert.IsTrue(capturedEntry.HasValue, "Event arg should be set.");
            Assert.AreEqual("first", capturedEntry!.Value.Reason, "Event entry Reason mismatch.");
        }

        // ─── B ───────────────────────────────────────────────────────────────────

        [Test]
        public void Record_RingBuffer_DropsOldestPastCapacity()
        {
            // Arrange
            int total = SaveTelemetry.Capacity + 5;

            // Act
            for (int i = 0; i < total; i++)
                SaveTelemetry.Record(MakeEntry($"reason_{i}"));

            // Assert
            var snapshot = SaveTelemetry.Snapshot();

            Assert.AreEqual(SaveTelemetry.Capacity, snapshot.Count,
                "Snapshot should be capped at Capacity.");

            // Oldest 5 (reason_0 … reason_4) must have been evicted
            for (int i = 0; i < 5; i++)
            {
                string evictedReason = $"reason_{i}";
                Assert.IsFalse(snapshot.Exists(e => e.Reason == evictedReason),
                    $"Oldest entry '{evictedReason}' should have been dropped.");
            }

            // Newest entry is at the last index
            Assert.AreEqual($"reason_{total - 1}", snapshot[SaveTelemetry.Capacity - 1].Reason,
                "Last snapshot entry should be the most-recently recorded.");

            Assert.AreEqual(total, SaveTelemetry.TotalRecorded,
                "TotalRecorded must count every Record call, even evicted ones.");
        }

        // ─── C ───────────────────────────────────────────────────────────────────

        [Test]
        public void Record_Order_OldestToNewest()
        {
            // Act
            SaveTelemetry.Record(MakeEntry("a"));
            SaveTelemetry.Record(MakeEntry("b"));
            SaveTelemetry.Record(MakeEntry("c"));

            // Assert
            var snapshot = SaveTelemetry.Snapshot();
            Assert.AreEqual(3, snapshot.Count);
            Assert.AreEqual("a", snapshot[0].Reason, "Index 0 should be oldest.");
            Assert.AreEqual("b", snapshot[1].Reason, "Index 1 should be middle.");
            Assert.AreEqual("c", snapshot[2].Reason, "Index 2 should be newest.");
        }

        // ─── D ───────────────────────────────────────────────────────────────────

        [Test]
        public void Snapshot_ReturnsCopy_ModifyingItDoesNotAffectBuffer()
        {
            // Arrange
            SaveTelemetry.Record(MakeEntry("original"));

            // Act — mutate the first snapshot
            var snapshot1 = SaveTelemetry.Snapshot();
            snapshot1.Add(MakeEntry("injected"));
            snapshot1.Clear();

            // Assert — a fresh snapshot is unaffected
            var snapshot2 = SaveTelemetry.Snapshot();
            Assert.AreEqual(1, snapshot2.Count,
                "Mutating the first snapshot must not change the internal buffer.");
            Assert.AreEqual("original", snapshot2[0].Reason);
        }

        // ─── E ───────────────────────────────────────────────────────────────────

        [Test]
        public void Record_FromMultipleThreads_BufferStaysConsistent()
        {
            // Arrange
            const int threadCount = 8;
            const int recordsPerThread = 50;

            // Act
            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < recordsPerThread; i++)
                        SaveTelemetry.Record(MakeEntry($"thread_{threadId}_entry_{i}"));
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks),
                "Concurrent Record calls must not throw.");

            // Assert
            int expectedTotal = threadCount * recordsPerThread;
            int expectedSnapshot = Math.Min(expectedTotal, SaveTelemetry.Capacity);

            Assert.AreEqual(expectedTotal, SaveTelemetry.TotalRecorded,
                "TotalRecorded must account for every Record call across all threads.");

            var snapshot = SaveTelemetry.Snapshot();
            Assert.AreEqual(expectedSnapshot, snapshot.Count,
                "Snapshot size must equal Min(total, Capacity).");
        }

        // ─── F ───────────────────────────────────────────────────────────────────

        [Test]
        public void Record_FaultySubscriber_DoesNotPropagate()
        {
            // Arrange
            _subscribedHandler = _ => throw new InvalidOperationException("Subscriber kaboom");
            SaveTelemetry.OnEntryRecorded += _subscribedHandler;

            var entry = MakeEntry("faulty_subscriber_test");

            // Act + Assert — must not throw
            Assert.DoesNotThrow(() => SaveTelemetry.Record(entry),
                "A throwing subscriber must be swallowed by the production try/catch.");

            // Entry must still be in the buffer
            var snapshot = SaveTelemetry.Snapshot();
            Assert.AreEqual(1, snapshot.Count, "Entry must have been recorded despite subscriber throwing.");
            Assert.AreEqual("faulty_subscriber_test", snapshot[0].Reason);
        }

        // ─── G ───────────────────────────────────────────────────────────────────

        [Test]
        public void ResetForTests_ClearsBufferAndCounter()
        {
            // Arrange — populate the buffer
            for (int i = 0; i < 10; i++)
                SaveTelemetry.Record(MakeEntry($"entry_{i}"));

            // Act
            SaveTelemetry.ResetForTests();

            // Assert
            Assert.AreEqual(0, SaveTelemetry.TotalRecorded,
                "TotalRecorded must be 0 after reset.");
            Assert.AreEqual(0, SaveTelemetry.Snapshot().Count,
                "Snapshot must be empty after reset.");
        }

        // ─── H ───────────────────────────────────────────────────────────────────

        [Test]
        public void Entry_Timestamp_IsRecentNow()
        {
            // Arrange
            var before = DateTime.Now;

            // Act
            var entry = new SaveTelemetryEntry(
                SaveTelemetryEntry.SaveKind.Autosave, "timestamp_check",
                true, 0, 0.0, string.Empty, false);

            var after = DateTime.Now;

            // Assert — Timestamp must fall within the ±2-second window
            Assert.That(entry.Timestamp, Is.GreaterThanOrEqualTo(before.AddSeconds(-2)),
                "Timestamp should not be before test start.");
            Assert.That(entry.Timestamp, Is.LessThanOrEqualTo(after.AddSeconds(2)),
                "Timestamp should not be more than 2 s after construction.");
        }
    }
}
