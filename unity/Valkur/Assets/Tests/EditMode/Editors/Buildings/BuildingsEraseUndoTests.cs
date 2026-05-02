using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Verifies the Erase tool's undo/redo contract end-to-end using the same
    /// Destroy/Recreate pattern <see cref="BuildingsRuntimeEditor.CommitErase"/> uses.
    ///
    /// CommitErase wraps a (do, undo) pair in BuildingsRuntimeEditor.ExecutePersistedEdit
    /// which delegates to <see cref="UndoStack"/>. Persistence side-effects are orthogonal
    /// to the undo/redo contract.
    ///
    /// Pattern:
    ///   - Snapshot full per-instance state up front (Template, Pos, Zone, IDs, overrides).
    ///   - Do:   destroy each live BuildingObject.
    ///   - Undo: re-create new BuildingObjects from snapshots; track them as the new "live" set
    ///           so a subsequent Redo destroys those instead.
    ///
    /// These tests exercise the cycle directly through an UndoStack to assert:
    ///
    ///   1. After Do:    every targeted BuildingObject is destroyed (Unity null).
    ///   2. After Undo:  N new BuildingObjects exist with state matching the snapshots.
    ///   3. After Redo:  the recreated instances are destroyed (full round-trip).
    ///   4. Round-trip Undo/Redo preserves Template / position / zone / instanceId.
    ///   5. A fresh Do clears the redo stack.
    /// </summary>
    [TestFixture]
    public class BuildingsEraseUndoTests
    {
        private readonly List<GameObject>       _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets       = new List<ScriptableObject>();

        private static readonly FieldInfo s_templateField =
            typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp] public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
        }

        private BuildingTemplateData CreateTemplate(int templateId)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = templateId;
            t.originalScale = new Vector2Int(32, 32);
            _assets.Add(t);
            return t;
        }

        private BuildingObject CreateBuilding(BuildingTemplateData template, string zone, Vector3 worldPos, int instanceId)
        {
            var go = new GameObject($"B_{instanceId}");
            go.transform.position = worldPos;
            _sceneObjects.Add(go);
            var b = go.AddComponent<BuildingObject>();
            s_templateField.SetValue(b, template);
            b.ZoneName  = zone;
            b.InstanceId = instanceId;
            return b;
        }

        /// <summary>
        /// Snapshot used by the test command to capture per-instance state up front.
        /// Mirrors the EraseSnapshot class inside BuildingsRuntimeEditor.Erase.cs.
        /// </summary>
        private sealed class TestSnapshot
        {
            public BuildingTemplateData Template;
            public Vector3   Pos;
            public string    Zone;
            public int       InstanceId;
            public string    Name;
        }

        /// <summary>
        /// Reproduces the do/undo lambda pair built by CommitErase. Uses DestroyImmediate
        /// (instead of Destroy) so destruction is synchronous in EditMode. Tracks live
        /// instances in <paramref name="liveTargets"/> so Redo destroys the recreated set.
        /// Also records each created GameObject in <paramref name="trackedSceneObjects"/>
        /// so the test fixture can clean them up in TearDown.
        /// </summary>
        private static UndoStack.LambdaCommand BuildEraseCommand(
            List<BuildingObject> liveTargets,
            List<TestSnapshot>   snapshots,
            List<GameObject>     trackedSceneObjects)
        {
            return new UndoStack.LambdaCommand($"Erase {snapshots.Count} buildings",
                () =>
                {
                    for (int i = 0; i < liveTargets.Count; i++)
                    {
                        var bo = liveTargets[i];
                        if (bo == null) continue;
                        UnityEngine.Object.DestroyImmediate(bo.gameObject);
                    }
                    liveTargets.Clear();
                },
                () =>
                {
                    liveTargets.Clear();
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        var s = snapshots[i];
                        if (s.Template == null) continue;
                        var go = new GameObject(s.Name);
                        go.transform.position = s.Pos;
                        var b = go.AddComponent<BuildingObject>();
                        s_templateField.SetValue(b, s.Template);
                        b.ZoneName   = s.Zone;
                        b.InstanceId = s.InstanceId;
                        liveTargets.Add(b);
                        trackedSceneObjects.Add(go);
                    }
                });
        }

        private static List<TestSnapshot> Snapshot(IList<BuildingObject> matches)
        {
            var list = new List<TestSnapshot>(matches.Count);
            for (int i = 0; i < matches.Count; i++)
            {
                var b = matches[i];
                if (b == null || b.Template == null) continue;
                list.Add(new TestSnapshot
                {
                    Template   = b.Template,
                    Pos        = b.transform.position,
                    Zone       = b.ZoneName,
                    InstanceId = b.InstanceId,
                    Name       = b.gameObject.name,
                });
            }
            return list;
        }

        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void Erase_Do_DestroysAllMatches()
        {
            var t  = CreateTemplate(1);
            var b1 = CreateBuilding(t, "z", Vector3.zero,        1);
            var b2 = CreateBuilding(t, "z", new Vector3(2,2,0),  2);
            var b3 = CreateBuilding(t, "z", new Vector3(4,4,0),  3);

            var live      = new List<BuildingObject> { b1, b2, b3 };
            var snapshots = Snapshot(live);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(live, snapshots, _sceneObjects));

            // Unity-null check: after DestroyImmediate the references are == null.
            Assert.IsTrue(b1 == null);
            Assert.IsTrue(b2 == null);
            Assert.IsTrue(b3 == null);
            Assert.AreEqual(0, live.Count);
            Assert.AreEqual(1, undo.UndoCount);
            Assert.AreEqual(0, undo.RedoCount);
        }

        [Test]
        public void Erase_Undo_RecreatesAllMatchesWithSameState()
        {
            var t  = CreateTemplate(7);
            var b1 = CreateBuilding(t, "zoneA", new Vector3(1,1,0), 100);
            var b2 = CreateBuilding(t, "zoneA", new Vector3(3,3,0), 200);

            var live      = new List<BuildingObject> { b1, b2 };
            var snapshots = Snapshot(live);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(live, snapshots, _sceneObjects));
            Assert.AreEqual(0, live.Count);

            Assert.IsTrue(undo.Undo(), "Undo should succeed.");
            Assert.AreEqual(2, live.Count);
            // Both recreated instances must carry the same Template + Zone + InstanceId.
            Assert.AreSame(t, live[0].Template);
            Assert.AreSame(t, live[1].Template);
            CollectionAssert.AreEquivalent(new[] { 100, 200 },
                new[] { live[0].InstanceId, live[1].InstanceId });
            // Positions also restored verbatim.
            var positions = new HashSet<Vector3> { live[0].transform.position, live[1].transform.position };
            Assert.IsTrue(positions.Contains(new Vector3(1,1,0)));
            Assert.IsTrue(positions.Contains(new Vector3(3,3,0)));
            Assert.AreEqual(0, undo.UndoCount);
            Assert.AreEqual(1, undo.RedoCount);
        }

        [Test]
        public void Erase_Redo_DestroysRecreatedInstances()
        {
            var t  = CreateTemplate(1);
            var b1 = CreateBuilding(t, "z", Vector3.zero, 1);
            var b2 = CreateBuilding(t, "z", new Vector3(2,2,0), 2);

            var live      = new List<BuildingObject> { b1, b2 };
            var snapshots = Snapshot(live);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(live, snapshots, _sceneObjects));
            undo.Undo();
            Assert.AreEqual(2, live.Count);
            var recreated = new List<BuildingObject>(live);

            Assert.IsTrue(undo.Redo(), "Redo should succeed.");
            Assert.IsTrue(recreated[0] == null);
            Assert.IsTrue(recreated[1] == null);
            Assert.AreEqual(0, live.Count);
            Assert.AreEqual(1, undo.UndoCount);
            Assert.AreEqual(0, undo.RedoCount);
        }

        [Test]
        public void Erase_UndoRedoRoundTrip_IsIdempotent()
        {
            var t  = CreateTemplate(1);
            var b1 = CreateBuilding(t, "z", new Vector3(1,1,0), 1);
            var b2 = CreateBuilding(t, "z", new Vector3(3,3,0), 2);
            var b3 = CreateBuilding(t, "z", new Vector3(5,5,0), 3);

            var live      = new List<BuildingObject> { b1, b2, b3 };
            var snapshots = Snapshot(live);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(live, snapshots, _sceneObjects));

            // Two full undo/redo cycles. Each Undo creates 3 new BuildingObjects;
            // each Redo destroys them. Verify the count + state holds.
            for (int cycle = 0; cycle < 2; cycle++)
            {
                undo.Undo();
                Assert.AreEqual(3, live.Count, $"Cycle {cycle}: 3 buildings re-created on Undo.");
                CollectionAssert.AreEquivalent(new[] { 1, 2, 3 },
                    new[] { live[0].InstanceId, live[1].InstanceId, live[2].InstanceId });

                undo.Redo();
                Assert.AreEqual(0, live.Count, $"Cycle {cycle}: all destroyed on Redo.");
            }
        }

        [Test]
        public void Erase_NewDo_ClearsRedoStack()
        {
            var t  = CreateTemplate(1);
            var b1 = CreateBuilding(t, "z", Vector3.zero, 1);
            var b2 = CreateBuilding(t, "z", new Vector3(2,2,0), 2);

            // First batch erases b1.
            var live1      = new List<BuildingObject> { b1 };
            var snapshots1 = Snapshot(live1);
            // Second batch erases b2.
            var live2      = new List<BuildingObject> { b2 };
            var snapshots2 = Snapshot(live2);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(live1, snapshots1, _sceneObjects));
            undo.Undo();   // pushes batch 1 to redo
            Assert.AreEqual(1, undo.RedoCount);

            undo.Do(BuildEraseCommand(live2, snapshots2, _sceneObjects));
            Assert.AreEqual(0, undo.RedoCount,
                "A new Do must clear the redo stack — standard UndoStack contract.");
            Assert.IsTrue(b2 == null,    "Second batch's building destroyed.");
            Assert.AreEqual(1, live1.Count, "First batch's recreated instance still alive.");
        }

        [Test]
        public void Erase_MultipleBatchesIndependentlyUndoable()
        {
            var t  = CreateTemplate(1);
            var b1 = CreateBuilding(t, "z", Vector3.zero,        1);
            var b2 = CreateBuilding(t, "z", new Vector3(2,2,0),  2);
            var b3 = CreateBuilding(t, "z", new Vector3(4,4,0),  3);

            var liveA      = new List<BuildingObject> { b1 };
            var snapshotsA = Snapshot(liveA);
            var liveB      = new List<BuildingObject> { b2, b3 };
            var snapshotsB = Snapshot(liveB);

            var undo = new UndoStack(capacity: 8);
            undo.Do(BuildEraseCommand(liveA, snapshotsA, _sceneObjects));
            undo.Do(BuildEraseCommand(liveB, snapshotsB, _sceneObjects));
            Assert.IsTrue(b1 == null);
            Assert.IsTrue(b2 == null);
            Assert.IsTrue(b3 == null);

            // Single Undo only reverts batch B — batch A stays erased.
            undo.Undo();
            Assert.AreEqual(0, liveA.Count, "Batch A unaffected by single Undo.");
            Assert.AreEqual(2, liveB.Count, "Batch B's two instances re-created.");

            // Second Undo reverts batch A.
            undo.Undo();
            Assert.AreEqual(1, liveA.Count, "Batch A re-created on second Undo.");
            Assert.AreEqual(0, undo.UndoCount);
        }
    }
}
