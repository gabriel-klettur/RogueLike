using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Tests for the collider brush Paint / Erase pipeline in
    /// <see cref="BuildingsRuntimeEditor"/>.
    ///
    /// Python reference: <c>roguelike_editors/buildings/collision_editor.py</c>
    /// paint_cell / erase_cell methods.
    ///
    /// Coverage:
    ///   GROUP 1 — ApplyGridOverrideToBuilding (main bug fix)
    ///   • All-walkable grid disables the root BoxCollider2D.
    ///   • Solid cells → CollTile children created, root disabled.
    ///   • null grid → root re-enabled for solid buildings.
    ///
    ///   GROUP 2 — HandleColliderPaint (Solid / Walk modes)
    ///   • Solid mode sets hit cell to "#"; Walk (erase) sets to ".".
    ///   • Brush Off / outside rect → no-op.
    ///   • Brush size 2 paints adjacent cells.
    ///
    ///   GROUP 3 — BeginColliderStroke / EndColliderStroke
    ///   • Begin records before-snapshot; End registers undo only when cells changed.
    ///
    ///   GROUP 4 — EnsureActiveColliderSession scope resolution
    ///   • CG uses imageKey; CU uses instanceId; session is cached.
    ///
    ///   GROUP 5 — PersistSessionToStore
    ///   • CG writes to image store; CU writes to instance store.
    ///
    ///   GROUP 6 — Full paint → store → apply round-trip (integration)
    ///   • Erasing all cells disables root BoxCollider2D (regression).
    ///
    ///   GROUP 7 — SetBrushAction self-toggle
    ///   • Clicking active Paint/Erase button toggles brush off.
    ///   • Clicking Erase while Paint is active switches mode.
    ///
    ///   GROUP 8 — CollTile positioning and sizing
    ///   • Tile world positions match expected cell center coordinates.
    ///   • Tile BoxCollider2D size matches cell world size.
    ///   • Tile count matches solid cell count; re-apply updates positions.
    ///
    ///   GROUP 9 — Collider save: store population via stroke pipeline
    ///   • Stroke end with change populates CU instance store.
    ///   • Stroke end with change populates CG image store.
    ///   • Stroke end without change leaves stores empty.
    /// </summary>
    [TestFixture]
    public class ColliderBrushTests
    {
        // ── private nested types accessed via reflection ───────────────────────────

        private static readonly Type s_editorType   = typeof(BuildingsRuntimeEditor);
        private static readonly Type s_gridType      = s_editorType.GetNestedType("ColliderGridData",          BindingFlags.NonPublic);
        private static readonly Type s_sessionType   = s_editorType.GetNestedType("ActiveColliderGridSession", BindingFlags.NonPublic);
        private static readonly Type s_strokeType    = s_editorType.GetNestedType("ColliderPaintStroke",       BindingFlags.NonPublic);

        // ── scene & asset tracking ─────────────────────────────────────────────────

        private readonly List<GameObject>     _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // ── reflection helpers ────────────────────────────────────────────────────

        private static FieldInfo Field(object obj, string name) =>
            Field(obj.GetType(), name);

        private static FieldInfo Field(Type type, string name)
        {
            var t = type;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static MethodInfo Method(Type type, string name, Type[] paramTypes = null)
        {
            var t = type;
            while (t != null)
            {
                MethodInfo m;
                if (paramTypes == null)
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static);
                else
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static,
                                    null, paramTypes, null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        // ── object factories ──────────────────────────────────────────────────────

        private BuildingsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            var go = new GameObject("TestEditor");
            _scene.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            // Skip full BuildUI — just mark data as loaded so file I/O is bypassed.
            Field(ed, "_colliderDataLoaded")?.SetValue(ed, true);
            return ed;
        }

        /// <summary>
        /// Creates a BuildingObject with a solid template (64×64 px, no renderers).
        /// World rect: Rect(-1, 0, 2, 2) when at origin — 2×2 cell grid (32 px PPU).
        /// </summary>
        private BuildingObject CreateSolidBuilding(string imageKey = "assets/buildings/test.png",
                                                   bool solid = true, string scope = "CG", int instanceId = 1)
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.originalScale   = new Vector2Int(64, 64);
            template.solid           = solid;
            template.colliderScope   = scope;
            template.sourceImagePath = imageKey;
            _assets.Add(template);

            var go = new GameObject("Building");
            go.transform.position = Vector3.zero;
            var box = go.AddComponent<BoxCollider2D>();
            box.enabled = solid;
            var b = go.AddComponent<BuildingObject>();
            // Inject template + instanceId (both are serialised fields, set via reflection)
            Field(b, "_template")?.SetValue(b, template);
            Field(b, "_instanceId")?.SetValue(b, instanceId);
            if (scope == "CU")
                b.ColliderScopeOverride = "CU";
            _scene.Add(go);
            return b;
        }

        /// <summary>Creates a ColliderGridData (private inner class) via reflection.</summary>
        private static object MakeGrid(int cols, int rows, string fill = ".", Vector2Int refSize = default)
        {
            Assert.IsNotNull(s_gridType, "ColliderGridData nested type not found");
            var grid = Activator.CreateInstance(s_gridType);
            s_gridType.GetField("width").SetValue(grid,  cols);
            s_gridType.GetField("height").SetValue(grid, rows);
            var collision = new string[rows][];
            for (int r = 0; r < rows; r++)
            {
                collision[r] = new string[cols];
                for (int c = 0; c < cols; c++)
                    collision[r][c] = fill;
            }
            s_gridType.GetField("collision").SetValue(grid, collision);
            s_gridType.GetField("gridRefSize").SetValue(grid, refSize == default ? new Vector2Int(cols * 32, rows * 32) : refSize);
            return grid;
        }

        /// <summary>Sets a single cell in a ColliderGridData (via reflection).</summary>
        private static void SetCell(object grid, int row, int col, string value)
        {
            var collision = (string[][])s_gridType.GetField("collision").GetValue(grid);
            collision[row][col] = value;
        }

        private static string GetCell(object grid, int row, int col)
        {
            var collision = (string[][])s_gridType.GetField("collision").GetValue(grid);
            return collision[row][col];
        }

        // ── setup / teardown ──────────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
            LogAssert.ignoreFailingMessages = true;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 1 — ApplyGridOverrideToBuilding (main bug fix)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// BUG FIX: an authored grid with ZERO solid cells must disable the root
        /// BoxCollider2D rather than silently restoring it (previous behaviour).
        /// Python equivalent: erasing every cell in the collision editor must make
        /// the building fully walk-through.
        /// </summary>
        [Test]
        public void ApplyGridOverride_AllWalkable_DisablesRootBoxCollider()
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: true);
            var box      = building.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(box, "BoxCollider2D must exist on the building");
            // Confirm the box starts enabled (it's a solid building by default).
            box.enabled = true;

            // All-walkable 2×2 grid (no "#" cells) — this is the "user erased everything" state.
            var allWalkable = MakeGrid(2, 2, fill: ".");

            var applyMethod = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });
            Assert.IsNotNull(applyMethod, "ApplyGridOverrideToBuilding method not found");

            applyMethod.Invoke(ed, new[] { building, allWalkable });

            Assert.IsFalse(box.enabled,
                "Root BoxCollider2D must be DISABLED when the authored grid has zero solid cells " +
                "(user intentionally erased all collision).");
        }

        [Test]
        public void ApplyGridOverride_SomeSolidCells_CreatesCollTilesAndDisablesRoot()
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: true);
            var box      = building.GetComponent<BoxCollider2D>();
            box.enabled = true;

            // 2×2 grid with one "#" cell.
            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 0, 0, "#");

            var applyMethod = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });

            applyMethod.Invoke(ed, new[] { building, grid });

            Assert.IsFalse(box.enabled,
                "Root BoxCollider2D must be disabled when CollTile children handle collision.");

            // Exactly one CollTile child should have been created for the solid cell.
            int collTiles = 0;
            for (int i = 0; i < building.transform.childCount; i++)
            {
                var child = building.transform.GetChild(i);
                if (child.name.StartsWith("CollTile_") && child.gameObject.activeSelf)
                    collTiles++;
            }
            Assert.AreEqual(1, collTiles, "Exactly one active CollTile child expected for one '#' cell.");
        }

        [Test]
        public void ApplyGridOverride_NullGrid_EnablesRootForSolidBuilding()
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: true);
            var box      = building.GetComponent<BoxCollider2D>();
            box.enabled = false; // Start disabled; null grid should restore it.

            var applyMethod = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });

            applyMethod.Invoke(ed, new object[] { building, null });

            Assert.IsTrue(box.enabled,
                "Root BoxCollider2D must be re-enabled when grid is null (no override).");
        }

        [Test]
        public void ApplyGridOverride_NullGrid_LeavesRootDisabledForNonSolidBuilding()
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false);
            var box      = building.GetComponent<BoxCollider2D>();
            box.enabled = false;

            var applyMethod = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });

            applyMethod.Invoke(ed, new object[] { building, null });

            Assert.IsFalse(box.enabled,
                "Root BoxCollider2D stays disabled for non-solid buildings with null grid.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 2 — HandleColliderPaint (Solid / Walk modes)
        // ═══════════════════════════════════════════════════════════════════════════

        // Helper: sets up editor + building + brush mode, invokes HandleColliderPaint.
        private (BuildingsRuntimeEditor ed, BuildingObject building) SetupForPaint(
            string brushMode, int brushSize = 1, bool solidBuilding = false,
            string scope = "CG", int instanceId = 1)
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: solidBuilding, scope: scope, instanceId: instanceId);

            // Map brush mode enum value by name.
            var modeField = Field(ed, "_collBrushMode");
            var modeType  = modeField.FieldType; // CollBrushMode enum
            modeField.SetValue(ed, Enum.Parse(modeType, brushMode));

            Field(ed, "_activeBuilding")?.SetValue(ed, building);
            Field(ed, "_collBrushSize")?.SetValue(ed, brushSize);
            return (ed, building);
        }

        private object InvokePaint(BuildingsRuntimeEditor ed, Vector3 worldPos)
        {
            var paintMethod = Method(s_editorType, "HandleColliderPaint",
                new[] { typeof(Vector3) });
            Assert.IsNotNull(paintMethod, "HandleColliderPaint method not found");
            return paintMethod.Invoke(ed, new object[] { worldPos });
        }

        private object GetSession(BuildingsRuntimeEditor ed)
        {
            var ensureSession = Method(s_editorType, "EnsureActiveColliderSession", Type.EmptyTypes);
            return ensureSession?.Invoke(ed, null);
        }

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
            // Building rect: Rect(-1, 0, 2, 2) — click at y=-1 is below yMin.
            InvokePaint(ed, new Vector3(0f, -1f, 0f));

            var session = GetSession(ed);
            if (session == null) return; // No session = no paint = correct.
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);
            // All cells should remain "#" for a solid building.
            int changed = 0;
            var collision = (string[][])s_gridType.GetField("collision").GetValue(grid);
            foreach (var row in collision)
                foreach (var cell in row)
                    if (cell != "#") changed++;
            Assert.AreEqual(0, changed, "No cells should change when clicking outside the building rect.");
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

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 4 — EnsureActiveColliderSession scope resolution
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void EnsureActiveColliderSession_CG_UsesImageKey()
        {
            const string imgPath = "assets/buildings/wall.png";
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CG");
            // Override sourceImagePath on the template.
            var templateField = typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var template = (BuildingTemplateData)templateField.GetValue(building);
            template.sourceImagePath = imgPath;

            var session = GetSession(ed);
            Assert.IsNotNull(session);

            var imageKey = (string)s_sessionType.GetField("ImageKey").GetValue(session);
            var scope    = s_sessionType.GetField("Scope").GetValue(session);
            var scopeType = s_sessionType.GetField("Scope").FieldType;
            var cgValue  = Enum.Parse(scopeType, "CG");

            Assert.AreEqual(imgPath.Replace("\\", "/"), imageKey,
                "ImageKey must be the normalised sourceImagePath for CG scope.");
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
            // Ensure imageKey is non-empty so PersistSessionToStore writes the store.
            var templateField = typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var template = (BuildingTemplateData)templateField.GetValue(building);
            template.sourceImagePath = "assets/buildings/solid_wall.png";

            InvokePaint(ed, new Vector3(0f, 1f, 0f));

            // Check that _colliderImageStore has the painted (updated) grid.
            var storeField = Field(ed, "_colliderImageStore");
            var dict = storeField.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsTrue(dict.Contains("assets/buildings/solid_wall.png"),
                "After painting, image store must contain the key for this building's image.");

            // At least one cell must be "." (the erased cell).
            var storedGrid = dict["assets/buildings/solid_wall.png"];
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

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 7 — SetBrushAction self-toggle
        // ═══════════════════════════════════════════════════════════════════════════

        private static void InvokeSetBrushAction(BuildingsRuntimeEditor ed, string modeName)
        {
            var modeType  = Field(ed, "_collBrushMode").FieldType;
            var modeValue = Enum.Parse(modeType, modeName);
            var method    = Method(s_editorType, "SetBrushAction", new[] { modeType });
            Assert.IsNotNull(method, "SetBrushAction method not found");
            method.Invoke(ed, new[] { modeValue });
        }

        private static string GetBrushModeName(BuildingsRuntimeEditor ed)
        {
            var modeField = Field(ed, "_collBrushMode");
            return modeField.GetValue(ed).ToString();
        }

        [Test]
        public void SetBrushAction_Solid_WhenSolidActive_TurnsBrushOff()
        {
            var ed = CreateEditor();
            // Start with brush already in Solid mode.
            var modeField = Field(ed, "_collBrushMode");
            modeField.SetValue(ed, Enum.Parse(modeField.FieldType, "Solid"));

            InvokeSetBrushAction(ed, "Solid"); // click same button again

            Assert.AreEqual("Off", GetBrushModeName(ed),
                "Clicking the active Paint button again must toggle brush off.");
        }

        [Test]
        public void SetBrushAction_Walk_WhenWalkActive_TurnsBrushOff()
        {
            var ed = CreateEditor();
            var modeField = Field(ed, "_collBrushMode");
            modeField.SetValue(ed, Enum.Parse(modeField.FieldType, "Walk"));

            InvokeSetBrushAction(ed, "Walk");

            Assert.AreEqual("Off", GetBrushModeName(ed),
                "Clicking the active Erase button again must toggle brush off.");
        }

        [Test]
        public void SetBrushAction_Solid_WhenOff_ActivatesSolid()
        {
            var ed = CreateEditor();
            // Brush starts Off.
            Assert.AreEqual("Off", GetBrushModeName(ed));

            InvokeSetBrushAction(ed, "Solid");

            Assert.AreEqual("Solid", GetBrushModeName(ed),
                "Clicking Paint when brush is Off must activate Solid mode.");
        }

        [Test]
        public void SetBrushAction_Walk_WhenSolidActive_SwitchesToWalk()
        {
            var ed = CreateEditor();
            var modeField = Field(ed, "_collBrushMode");
            modeField.SetValue(ed, Enum.Parse(modeField.FieldType, "Solid"));

            InvokeSetBrushAction(ed, "Walk");

            Assert.AreEqual("Walk", GetBrushModeName(ed),
                "Clicking Erase while Paint is active must switch to Walk (erase) mode.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 8 — CollTile positioning & sizing
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // Building setup: 64×64 px, origin (0,0), PPU=32.
        //   WorldRect = Rect(-1, 0, 2, 2).
        //   2×2 cell grid, cellW = cellH = 1 world unit.
        //   Cell centers:
        //     [0][0] → (-0.5, 1.5)   [0][1] → (0.5, 1.5)
        //     [1][0] → (-0.5, 0.5)   [1][1] → (0.5, 0.5)
        // ═══════════════════════════════════════════════════════════════════════════

        private void ApplyGrid(BuildingsRuntimeEditor ed, BuildingObject building, object grid)
        {
            var m = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });
            Assert.IsNotNull(m, "ApplyGridOverrideToBuilding method not found");
            m.Invoke(ed, new[] { building, grid });
        }

        [Test]
        public void CollTile_Position_MatchesCellWorldCenter()
        {
            // Place a single "#" at [0][0] and verify its world position.
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false); // non-solid, starts all "."

            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 0, 0, "#"); // top-left cell

            ApplyGrid(ed, building, grid);

            var tile = building.transform.Find("CollTile_0_0");
            Assert.IsNotNull(tile, "CollTile_0_0 must exist for solid cell [0][0].");

            // localPosition equals worldPosition when building is at origin with scale (1,1,1).
            Vector3 worldPos = tile.position;
            Assert.AreEqual(-0.5f, worldPos.x, 0.01f, "CollTile_0_0 x must be -0.5 (cell [0][0] center).");
            Assert.AreEqual( 1.5f, worldPos.y, 0.01f, "CollTile_0_0 y must be 1.5 (cell [0][0] center).");
        }

        [Test]
        public void CollTile_Position_MatchesCellWorldCenter_BottomRightCell()
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false);

            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 1, 1, "#"); // bottom-right cell

            ApplyGrid(ed, building, grid);

            var tile = building.transform.Find("CollTile_1_1");
            Assert.IsNotNull(tile, "CollTile_1_1 must exist for solid cell [1][1].");

            Assert.AreEqual( 0.5f, tile.position.x, 0.01f, "CollTile_1_1 x must be 0.5.");
            Assert.AreEqual( 0.5f, tile.position.y, 0.01f, "CollTile_1_1 y must be 0.5.");
        }

        [Test]
        public void CollTile_Size_MatchesCellWorldSize()
        {
            // Each cell in the 2×2 grid is 1×1 world unit; BoxCollider2D.size must match.
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false);

            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 0, 0, "#");

            ApplyGrid(ed, building, grid);

            var tile = building.transform.Find("CollTile_0_0");
            Assert.IsNotNull(tile);
            var box = tile.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(box, "CollTile must have a BoxCollider2D.");
            Assert.AreEqual(1f, box.size.x, 0.01f, "CollTile BoxCollider2D width must be 1 world unit.");
            Assert.AreEqual(1f, box.size.y, 0.01f, "CollTile BoxCollider2D height must be 1 world unit.");
        }

        [Test]
        public void CollTile_Count_MatchesSolidCellCount()
        {
            // 3 solid cells in a 2×2 grid → exactly 3 active CollTile children.
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false);

            var grid = MakeGrid(2, 2, fill: ".");
            SetCell(grid, 0, 0, "#");
            SetCell(grid, 0, 1, "#");
            SetCell(grid, 1, 0, "#");
            // [1][1] stays "."

            ApplyGrid(ed, building, grid);

            int count = 0;
            for (int i = 0; i < building.transform.childCount; i++)
            {
                var child = building.transform.GetChild(i);
                if (child.name.StartsWith("CollTile_") && child.gameObject.activeSelf)
                    count++;
            }
            Assert.AreEqual(3, count, "Exactly 3 active CollTile children for 3 solid cells.");
        }

        [Test]
        public void CollTile_Reapply_UpdatesPositions()
        {
            // Apply solid at [0][0], then re-apply with solid at [1][1] instead.
            // Only CollTile_1_1 should remain active; CollTile_0_0 must be pooled/inactive.
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: false);

            var grid1 = MakeGrid(2, 2, fill: ".");
            SetCell(grid1, 0, 0, "#");
            ApplyGrid(ed, building, grid1);
            Assert.IsNotNull(building.transform.Find("CollTile_0_0"), "First apply: CollTile_0_0 must exist.");

            var grid2 = MakeGrid(2, 2, fill: ".");
            SetCell(grid2, 1, 1, "#");
            ApplyGrid(ed, building, grid2);

            var after00 = building.transform.Find("CollTile_0_0");
            var after11 = building.transform.Find("CollTile_1_1");

            // After re-apply, CollTile_0_0 should be pooled (inactive), CollTile_1_1 active.
            Assert.IsTrue(after00 == null || !after00.gameObject.activeSelf,
                "CollTile_0_0 must be pooled/inactive after re-apply with different grid.");
            Assert.IsNotNull(after11, "CollTile_1_1 must exist after re-apply.");
            Assert.IsTrue(after11.gameObject.activeSelf, "CollTile_1_1 must be active.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 9 — Collider save: store population via stroke pipeline
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void EndColliderStroke_WithChange_PopulatesCUStore()
        {
            // Verify that after a complete stroke (begin → paint → end), the
            // _colliderInstanceStore holds the updated grid — confirming the
            // data that would be written to JSON is correct.
            const int iid = 33;
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CU", instanceId: iid);

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var paintMethod = Method(s_editorType, "HandleColliderPaint",  new[] { typeof(Vector3) });
            var endMethod   = Method(s_editorType, "EndColliderStroke",    Type.EmptyTypes);

            beginMethod.Invoke(ed, null);
            paintMethod.Invoke(ed, new object[] { new Vector3(0f, 1f, 0f) }); // centre cell
            endMethod.Invoke(ed, null);

            var store = Field(ed, "_colliderInstanceStore")?.GetValue(ed)
                            as System.Collections.IDictionary;
            Assert.IsNotNull(store, "_colliderInstanceStore must exist");
            Assert.IsTrue(store.Contains(iid),
                "Instance store must contain the building's instanceId after stroke end.");

            var storedGrid = store[iid];
            Assert.IsNotNull(storedGrid, "Stored grid must not be null.");
            var collision = (string[][])s_gridType.GetField("collision").GetValue(storedGrid);
            bool hasHash = false;
            foreach (var row in collision)
                foreach (var cell in row)
                    if (cell == "#") { hasHash = true; break; }
            Assert.IsTrue(hasHash,
                "Stored grid must contain at least one '#' cell after painting.");
        }

        [Test]
        public void EndColliderStroke_WithChange_PopulatesCGImageStore()
        {
            const string imgKey = "assets/buildings/wall_cg.png";
            var (ed, building)  = SetupForPaint("Walk", solidBuilding: true, scope: "CG");
            var templateField   = typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var template = (BuildingTemplateData)templateField.GetValue(building);
            template.sourceImagePath = imgKey;

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var paintMethod = Method(s_editorType, "HandleColliderPaint",  new[] { typeof(Vector3) });
            var endMethod   = Method(s_editorType, "EndColliderStroke",    Type.EmptyTypes);

            beginMethod.Invoke(ed, null);
            paintMethod.Invoke(ed, new object[] { new Vector3(0f, 1f, 0f) });
            endMethod.Invoke(ed, null);

            var store = Field(ed, "_colliderImageStore")?.GetValue(ed)
                            as System.Collections.IDictionary;
            Assert.IsNotNull(store);
            Assert.IsTrue(store.Contains(imgKey),
                "Image store must contain the imageKey after CG stroke end.");
        }

        [Test]
        public void EndColliderStroke_NoChange_DoesNotPopulateStore()
        {
            // Begin + end without any paint → stores must remain empty.
            var (ed, building) = SetupForPaint("Solid", solidBuilding: false, scope: "CU", instanceId: 1);
            GetSession(ed); // ensure session

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var endMethod   = Method(s_editorType, "EndColliderStroke",   Type.EmptyTypes);

            beginMethod.Invoke(ed, null);
            endMethod.Invoke(ed, null); // no paint between begin/end

            var instanceStore = Field(ed, "_colliderInstanceStore")?.GetValue(ed)
                                    as System.Collections.IDictionary;
            Assert.IsNotNull(instanceStore);
            Assert.AreEqual(0, instanceStore.Count,
                "Instance store must remain empty when no cells were changed.");
        }
    }
}
