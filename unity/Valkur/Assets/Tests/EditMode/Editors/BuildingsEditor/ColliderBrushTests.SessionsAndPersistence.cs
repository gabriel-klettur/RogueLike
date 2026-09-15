using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.BuildingsEditor
{
    /// <summary>ColliderBrushTests: the sessions and persistence tests. SetUp, TearDown and helpers live in ColliderBrushTests.cs.</summary>
    public partial class ColliderBrushTests
    {
        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 4 — EnsureActiveColliderSession scope resolution
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void EnsureActiveColliderSession_CG_UsesSourceImageKey()
        {
            // SetupForPaint uses the default imageKey = "assets/buildings/test.png" for the building.
            const string expectedImageKey = "assets/buildings/test.png";
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CG");

            var session = GetSession(ed);
            Assert.IsNotNull(session);

            var imageKey  = (string)s_sessionType.GetField("ImageKey").GetValue(session);
            var scope     = s_sessionType.GetField("Scope").GetValue(session);
            var scopeType = s_sessionType.GetField("Scope").FieldType;
            var cgValue   = Enum.Parse(scopeType, "CG");

            Assert.AreEqual(expectedImageKey, imageKey,
                "ImageKey for CG scope must be the normalized sourceImagePath so all buildings sharing the same image share the same collider grid.");
            Assert.AreEqual(cgValue, scope, "Scope must be CG.");
        }

        [Test]
        public void EnsureActiveColliderSession_CU_UsesInstanceId()
        {
            const int iid = 42;
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CU", instanceId: iid);

            var session = GetSession(ed);
            Assert.IsNotNull(session);

            var instanceId = (int)s_sessionType.GetField("InstanceId").GetValue(session);
            var scope      = s_sessionType.GetField("Scope").GetValue(session);
            var scopeType  = s_sessionType.GetField("Scope").FieldType;
            var cuValue    = Enum.Parse(scopeType, "CU");

            Assert.AreEqual(iid, instanceId, "InstanceId must match the building's instance ID for CU scope.");
            Assert.AreEqual(cuValue, scope, "Scope must be CU.");
        }

        [Test]
        public void EnsureActiveColliderSession_ReturnsCachedInstance()
        {
            var (ed, building) = SetupForPaint("Solid");

            var session1 = GetSession(ed);
            var session2 = GetSession(ed);

            Assert.AreEqual(session1, session2,
                "EnsureActiveColliderSession must return the same cached instance on consecutive calls.");
        }

        [Test]
        public void EnsureActiveColliderSession_RebuildAfterSessionCleared()
        {
            var (ed, building) = SetupForPaint("Solid");

            var session1 = GetSession(ed);
            // Clear the cached session (simulates what ApplyGridSnapshot does).
            Field(ed, "_activeColliderSession")?.SetValue(ed, null);
            var session2 = GetSession(ed);

            Assert.IsNotNull(session2, "A new session must be built after the cache is cleared.");
            Assert.AreNotSame(session1, session2, "A different (new) session object must be returned.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 5 — PersistSessionToStore
        // ═══════════════════════════════════════════════════════════════════════════

        private static void InvokePersistSession(BuildingsRuntimeEditor ed, object session)
        {
            var m = Method(s_editorType, "PersistSessionToStore", new[] { s_sessionType });
            Assert.IsNotNull(m, "PersistSessionToStore method not found");
            m.Invoke(ed, new[] { session });
        }

        private static object MakeSession(string imageKey, int instanceId, object workingGrid, object scope)
        {
            var session = Activator.CreateInstance(s_sessionType);
            s_sessionType.GetField("ImageKey").SetValue(session, imageKey);
            s_sessionType.GetField("InstanceId").SetValue(session, instanceId);
            s_sessionType.GetField("WorkingGrid").SetValue(session, workingGrid);
            s_sessionType.GetField("Scope").SetValue(session, scope);
            return session;
        }

        private static object CgScope() =>
            Enum.Parse(s_sessionType.GetField("Scope").FieldType, "CG");

        private static object CuScope() =>
            Enum.Parse(s_sessionType.GetField("Scope").FieldType, "CU");

        [Test]
        public void PersistSessionToStore_CG_WritesGridToImageStore()
        {
            var ed = CreateEditor();
            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 0, 0, "#");
            var session = MakeSession("assets/buildings/test.png", 1, grid, CgScope());

            InvokePersistSession(ed, session);

            // Access _colliderImageStore via reflection.
            var storeField = Field(ed, "_colliderImageStore");
            Assert.IsNotNull(storeField, "_colliderImageStore field not found");
            var rawStore = storeField.GetValue(ed);
            // Use the IDictionary interface.
            var dict = rawStore as System.Collections.IDictionary;
            Assert.IsNotNull(dict, "_colliderImageStore must be an IDictionary");
            Assert.IsTrue(dict.Contains("assets/buildings/test.png"),
                "Image store must contain the imageKey after PersistSessionToStore for CG scope.");
        }

        [Test]
        public void PersistSessionToStore_CU_WritesGridToInstanceStore()
        {
            var ed = CreateEditor();
            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 1, 1, "#");
            const int iid = 99;
            var session = MakeSession("assets/buildings/test.png", iid, grid, CuScope());

            InvokePersistSession(ed, session);

            var storeField = Field(ed, "_colliderInstanceStore");
            var dict = storeField.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsNotNull(dict, "_colliderInstanceStore must be an IDictionary");
            Assert.IsTrue(dict.Contains(iid),
                "Instance store must contain the instanceId after PersistSessionToStore for CU scope.");
        }

        [Test]
        public void PersistSessionToStore_CG_EmptyImageKey_WritesNothing()
        {
            var ed = CreateEditor();
            var grid = MakeGrid(2, 2, fill: "#");
            // Empty imageKey → store must not be written (silent no-op).
            var session = MakeSession(string.Empty, 0, grid, CgScope());

            InvokePersistSession(ed, session);

            var storeField = Field(ed, "_colliderImageStore");
            var dict = storeField.GetValue(ed) as System.Collections.IDictionary;
            Assert.AreEqual(0, dict.Count,
                "Image store must remain empty when imageKey is empty for CG scope.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 6 — Full paint → store → apply round-trip (integration)
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void PaintEraseRoundTrip_CG_ErasedCellIsWalkableInStore()
        {
            // Solid building (CG, all "#" by default); erase center cell; store has updated grid.
            var (ed, building) = SetupForPaint("Walk", solidBuilding: true, scope: "CG");
            // Shared store key is sourceImagePath-based in CG mode (all buildings using the same
            // image file share a single collider grid, regardless of templateId).
            var templateField = typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var template = (BuildingTemplateData)templateField.GetValue(building);
            template.sourceImagePath = "assets/buildings/solid_wall.png";
            string sharedKey = "assets/buildings/solid_wall.png";

            InvokePaint(ed, new Vector3(0f, 1f, 0f));

            // Check that _colliderImageStore has the painted (updated) grid.
            var storeField = Field(ed, "_colliderImageStore");
            var dict = storeField.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsTrue(dict.Contains(sharedKey),
                "After painting, shared store must contain the key for this building template.");

            // At least one cell must be "." (the erased cell).
            var storedGrid = dict[sharedKey];
            var collision  = (string[][])s_gridType.GetField("collision").GetValue(storedGrid);
            bool hasDot = false;
            foreach (var row in collision)
                foreach (var cell in row)
                    if (cell == ".") { hasDot = true; break; }
            Assert.IsTrue(hasDot, "Stored grid must contain at least one '.' cell after erasing.");
        }

        [Test]
        public void PaintEraseRoundTrip_CU_PaintedCellIsHashInStore()
        {
            // Non-solid building (CU, all "." by default); paint center cell → store has "#".
            const int iid = 77;
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CU", instanceId: iid);

            InvokePaint(ed, new Vector3(0f, 1f, 0f));

            var storeField = Field(ed, "_colliderInstanceStore");
            var dict = storeField.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsTrue(dict.Contains(iid),
                "Instance store must contain the instanceId after painting for CU scope.");

            var storedGrid = dict[iid];
            var collision  = (string[][])s_gridType.GetField("collision").GetValue(storedGrid);
            bool hasHash = false;
            foreach (var row in collision)
                foreach (var cell in row)
                    if (cell == "#") { hasHash = true; break; }
            Assert.IsTrue(hasHash, "Stored grid must contain at least one '#' cell after painting.");
        }

        [Test]
        public void PaintEraseRoundTrip_AllCellsErased_RootColliderDisabled()
        {
            // Solid 2×2 building (CU scope to target single instance); erase every cell.
            // After erasing all, ApplyGridOverrideToBuilding must DISABLE the root BoxCollider2D
            // (the bug was that it was incorrectly RE-ENABLED when GridHasSolidCells returned false).
            const int iid = 55;
            var (ed, building) = SetupForPaint("Walk", solidBuilding: true, scope: "CU", instanceId: iid);
            var box = building.GetComponent<BoxCollider2D>();
            box.enabled = true;

            var paintMethod = Method(s_editorType, "HandleColliderPaint", new[] { typeof(Vector3) });

            // Erase all four cells of the 2×2 grid by painting all quadrants.
            paintMethod.Invoke(ed, new object[] { new Vector3(-0.8f, 1.8f, 0f) }); // [0][0]
            paintMethod.Invoke(ed, new object[] { new Vector3( 0.8f, 1.8f, 0f) }); // [0][1]
            paintMethod.Invoke(ed, new object[] { new Vector3(-0.8f, 0.2f, 0f) }); // [1][0]
            paintMethod.Invoke(ed, new object[] { new Vector3( 0.8f, 0.2f, 0f) }); // [1][1]

            Assert.IsFalse(box.enabled,
                "Root BoxCollider2D must be DISABLED after all cells are erased — " +
                "this was the main brush-erase bug (GridHasSolidCells early return).");
        }
    }
}
