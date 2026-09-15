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
    /// <summary>ColliderBrushTests: the paint and stroke tests. SetUp, TearDown and helpers live in ColliderBrushTests.cs.</summary>
    public partial class ColliderBrushTests
    {
        [Test]
        public void HandleColliderPaint_SolidMode_SetsCellToHash()
        {
            // Non-solid building: default grid starts all "." → paint one cell → expect "#".
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false);

            // Building at origin, rect = Rect(-1, 0, 2, 2).
            // Center click → u=0.5, v=0.5 → cell [1][1] in a 2×2 grid.
            InvokePaint(ed, new Vector3(0f, 1f, 0f));

            var session = GetSession(ed);
            Assert.IsNotNull(session, "Active collider session must exist after paint.");
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);
            Assert.AreEqual("#", GetCell(grid, 1, 1),
                "Cell [1][1] must be '#' after Solid brush paint.");
        }

        [Test]
        public void HandleColliderPaint_WalkMode_SetsCellToDot()
        {
            // Solid building: default grid starts all "#" → erase one cell → expect ".".
            var (ed, building) = SetupForPaint("Walk", solidBuilding: true);

            // Top-left area click: worldPos near top-left → row 0, col 0 in 2×2 grid.
            // v close to 1 → row 0; u close to 0 → col 0.
            InvokePaint(ed, new Vector3(-0.8f, 1.8f, 0f));

            var session = GetSession(ed);
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);
            Assert.AreEqual(".", GetCell(grid, 0, 0),
                "Cell [0][0] must be '.' after Walk (erase) brush paint on a solid building.");
        }

        [Test]
        public void HandleColliderPaint_BrushOff_CausesNoChange()
        {
            var (ed, building) = SetupForPaint("Off");
            InvokePaint(ed, new Vector3(0f, 1f, 0f));
            // Session should not exist: brush is Off → early return before EnsureActiveColliderSession.
            var session = Field(ed, "_activeColliderSession")?.GetValue(ed);
            Assert.IsNull(session, "No session should be created when brush is Off.");
        }

        [Test]
        public void HandleColliderPaint_OutsideBuildingRect_CausesNoChange()
        {
            // Solid building (all "#"); click well outside the building rect.
            var (ed, building) = SetupForPaint("Walk", solidBuilding: true);
            var beforeSession = GetSession(ed);
            var beforeGrid = s_sessionType.GetField("WorkingGrid").GetValue(beforeSession);
            string before = FlattenGrid(beforeGrid);

            // Building rect: Rect(-1, 0, 2, 2) — click at y=-1 is below yMin.
            InvokePaint(ed, new Vector3(0f, -1f, 0f));

            var session = GetSession(ed);
            if (session == null) return; // No session = no paint = correct.
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);
            Assert.AreEqual(before, FlattenGrid(grid),
                "No cells should change when clicking outside the building rect.");
        }

        [Test]
        public void HandleColliderPaint_BrushSize2_PaintsAdjacentCells()
        {
            // Non-solid building (all "."); brush size 2 at center → expect 2×2 patch = all "#".
            var (ed, building) = SetupForPaint("Solid", brushSize: 2, solidBuilding: false);

            // Click center of building: worldPos = (0, 1).
            // brush size 2: half=0, extra=1 → dr in [0,1], dc in [0,1] → cells [1][1],[1][2-clamped],[2-clamped][1] ...
            // For a 2-col, 2-row grid the brush will paint all reachable cells (col ± 0..1, row ± 0..1).
            InvokePaint(ed, new Vector3(0f, 1f, 0f));

            var session = GetSession(ed);
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);
            int solidCount = 0;
            var collision = (string[][])s_gridType.GetField("collision").GetValue(grid);
            foreach (var row in collision)
                foreach (var cell in row)
                    if (cell == "#") solidCount++;
            Assert.Greater(solidCount, 1,
                "Brush size 2 must paint more than one cell.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 3 — BeginColliderStroke / EndColliderStroke
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void BeginColliderStroke_RecordsBeforeSnapshot()
        {
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false);

            // Ensure a session exists first.
            var session = GetSession(ed);
            Assert.IsNotNull(session, "Session must exist to begin a stroke.");

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            Assert.IsNotNull(beginMethod, "BeginColliderStroke method not found");
            beginMethod.Invoke(ed, null);

            var stroke = Field(ed, "_colliderStroke")?.GetValue(ed);
            Assert.IsNotNull(stroke, "_colliderStroke must exist");
            var active = (bool)s_strokeType.GetField("Active").GetValue(stroke);
            var before = s_strokeType.GetField("Before").GetValue(stroke);

            Assert.IsTrue(active,  "Stroke must be active after BeginColliderStroke.");
            Assert.IsNotNull(before, "Stroke Before snapshot must be recorded.");
        }

        [Test]
        public void BeginColliderStroke_Idempotent_WhenAlreadyActive()
        {
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false);
            GetSession(ed); // ensure session

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            beginMethod.Invoke(ed, null);
            var stroke = Field(ed, "_colliderStroke")?.GetValue(ed);
            var before1 = s_strokeType.GetField("Before").GetValue(stroke);

            // Second begin on already-active stroke must be a no-op.
            beginMethod.Invoke(ed, null);
            var before2 = s_strokeType.GetField("Before").GetValue(stroke);
            Assert.AreEqual(before1, before2, "BeginColliderStroke must not overwrite Before snapshot if stroke is active.");
        }

        [Test]
        public void EndColliderStroke_NoUndoEntryIfUnchanged()
        {
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false);
            GetSession(ed);

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var endMethod   = Method(s_editorType, "EndColliderStroke",   Type.EmptyTypes);
            beginMethod.Invoke(ed, null);

            // Do NOT paint — stroke ends without changes.
            endMethod.Invoke(ed, null);

            var undo     = Field(ed, "_undo")?.GetValue(ed);
            var undoType = undo.GetType();
            int undoCount = (int)undoType.GetProperty("UndoCount").GetValue(undo);
            Assert.AreEqual(0, undoCount,
                "No undo entry should be created when the stroke made no changes.");
        }

        [Test]
        public void EndColliderStroke_CreatesUndoEntryWhenChanged()
        {
            // Non-solid building (all "."); paint one cell, then end stroke.
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false);
            GetSession(ed);

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var endMethod   = Method(s_editorType, "EndColliderStroke",   Type.EmptyTypes);
            var paintMethod = Method(s_editorType, "HandleColliderPaint", new[] { typeof(Vector3) });

            beginMethod.Invoke(ed, null);
            paintMethod.Invoke(ed, new object[] { new Vector3(0f, 1f, 0f) });
            endMethod.Invoke(ed, null);

            var undo     = Field(ed, "_undo")?.GetValue(ed);
            var undoType = undo.GetType();
            int undoCount = (int)undoType.GetProperty("UndoCount").GetValue(undo);
            Assert.AreEqual(1, undoCount,
                "Exactly one undo entry must be created after a stroke that changed cells.");
        }
    }
}
