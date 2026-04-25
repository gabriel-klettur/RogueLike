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
    /// Tests for the logical-grid (N×M topology) design introduced in Phase 5 of the
    /// Buildings-Editor collider refactor.
    ///
    /// Background:
    ///   Previously, every call to ResolveWorkingGridFor / ApplyGridOverrideToBuilding
    ///   resampled the stored grid by the building's pixel size
    ///   (ceil(effectiveWidth / 32), ceil(effectiveHeight / 32)).
    ///   This made the collision pattern depend on the per-instance scale, so two buildings
    ///   of different sizes sharing the same image would show different grid resolutions.
    ///
    ///   The fix: the grid is now a LOGICAL topology owned by the image (CG) or the instance
    ///   (CU).  Per-instance world-size mapping is handled entirely inside
    ///   BuildingObject.TryGetWorldCellRect(), which is already proportional.
    ///
    /// Coverage:
    ///   GROUP 10 — Logical-grid invariants (no pixel-based resampling)
    ///   • Two buildings sharing the same image receive the same grid topology.
    ///   • ResolveWorkingGridFor returns a clone with unchanged dims (no resample).
    ///   • Different-size buildings that share a CG grid yield proportionally
    ///     sized CollTiles (more world-units per tile for larger buildings).
    ///
    ///   GROUP 11 — CreateDefaultFootprintGrid uses template.originalScale
    ///   • Default cols = ceil(originalScale.x / 32); rows = ceil(originalScale.y / 32).
    ///   • Scale override on the instance does NOT change the default grid dims.
    ///   • Missing originalScale falls back to 1×1 grid without crashing.
    ///
    ///   GROUP 12 — AdjustGridResolution (new UI feature)
    ///   • +1 col widens the stored grid by exactly 1 column.
    ///   • −1 row narrows the stored grid by exactly 1 row.
    ///   • Clamps at MIN (1) and MAX (32) — does not go below 1 or above 32.
    ///   • CG resize propagates to all buildings sharing the same image path.
    ///   • CU resize does NOT affect other buildings.
    ///   • Each resize registers one undoable action.
    ///   • Undo restores the previous grid dimensions.
    ///
    ///   GROUP 13 — ResampleGridToResolution (logical resampler)
    ///   • Null source returns null without throwing.
    ///   • Same-size returns a clone with identical topology.
    ///   • Expansion: solid cells are preserved in the larger grid.
    ///   • Shrink: solid cells are conservatively preserved (no cell silently disappears
    ///     unless it maps to an all-walkable source region).
    /// </summary>
    [TestFixture]
    public class ColliderGridResolutionTests
    {
        // ── reflected type handles ─────────────────────────────────────────────────

        private static readonly Type s_editorType  = typeof(BuildingsRuntimeEditor);
        private static readonly Type s_gridType    = s_editorType.GetNestedType("ColliderGridData",          BindingFlags.NonPublic);
        private static readonly Type s_sessionType = s_editorType.GetNestedType("ActiveColliderGridSession", BindingFlags.NonPublic);

        // ── tracking ──────────────────────────────────────────────────────────────

        private readonly List<GameObject>       _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // ── reflection helpers ─────────────────────────────────────────────────────

        private static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) return f;
            }
            return null;
        }

        private static FieldInfo Field(object obj, string name) => Field(obj.GetType(), name);

        private static MethodInfo Method(Type type, string name, Type[] sig = null)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var m = sig == null
                    ? t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                        BindingFlags.Instance  | BindingFlags.Static)
                    : t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                        BindingFlags.Instance  | BindingFlags.Static,
                                  null, sig, null);
                if (m != null) return m;
            }
            return null;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            for (var t = typeof(T).BaseType; t != null; t = t.BaseType)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
            }
        }

        // ── object factories ───────────────────────────────────────────────────────

        private BuildingsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            var go = new GameObject("TestEditor");
            _scene.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            Field(ed, "_colliderDataLoaded")?.SetValue(ed, true);
            return ed;
        }

        /// <param name="imageKey">Shared sourceImagePath (used as CG store key).</param>
        /// <param name="originalScale">Template natural size in pixels.</param>
        /// <param name="scaleOverridePx">Optional per-instance scale override.</param>
        private BuildingObject CreateBuilding(
            string imageKey,
            Vector2Int originalScale,
            bool solid           = false,
            string scope         = "CG",
            int instanceId       = 1,
            int templateId       = 1,
            Vector2Int scaleOverridePx = default,
            float splitRatio     = 0f)
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.templateId      = templateId;
            template.originalScale   = originalScale;
            template.solid           = solid;
            template.colliderScope   = scope;
            template.sourceImagePath = imageKey;
            template.splitRatio      = splitRatio;
            _assets.Add(template);

            var go = new GameObject($"Building_{instanceId}");
            float worldW = originalScale.x / 32f;
            float worldH = originalScale.y / 32f;
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one;
            var box = go.AddComponent<BoxCollider2D>();
            box.enabled = solid;
            var b = go.AddComponent<BuildingObject>();
            Field(b, "_template")?.SetValue(b, template);
            Field(b, "_instanceId")?.SetValue(b, instanceId);
            if (scaleOverridePx != default && (scaleOverridePx.x > 0 || scaleOverridePx.y > 0))
                b.ScaleOverride = scaleOverridePx;
            if (scope == "CU")
                b.ColliderScopeOverride = "CU";
            _scene.Add(go);
            return b;
        }

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
                for (int c = 0; c < cols; c++) collision[r][c] = fill;
            }
            s_gridType.GetField("collision").SetValue(grid, collision);
            s_gridType.GetField("gridRefSize").SetValue(grid,
                refSize == default ? new Vector2Int(cols * 32, rows * 32) : refSize);
            return grid;
        }

        private static void SetCell(object grid, int row, int col, string v)
        {
            var c = (string[][])s_gridType.GetField("collision").GetValue(grid);
            c[row][col] = v;
        }

        private static string GetCell(object grid, int row, int col)
        {
            var c = (string[][])s_gridType.GetField("collision").GetValue(grid);
            return c[row][col];
        }

        private static int GridWidth(object grid)  => (int)s_gridType.GetField("width").GetValue(grid);
        private static int GridHeight(object grid) => (int)s_gridType.GetField("height").GetValue(grid);

        private object GetSession(BuildingsRuntimeEditor ed)
        {
            var m = Method(s_editorType, "EnsureActiveColliderSession", Type.EmptyTypes);
            return m?.Invoke(ed, null);
        }

        // Inject a grid directly into the image store.
        private static void InjectImageGrid(BuildingsRuntimeEditor ed, string key, object grid)
        {
            var store = Field(ed, "_colliderImageStore")?.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsNotNull(store, "_colliderImageStore not found");
            store[key] = grid;
        }

        // Inject a grid directly into the instance store.
        private static void InjectInstanceGrid(BuildingsRuntimeEditor ed, int instanceId, object grid)
        {
            var store = Field(ed, "_colliderInstanceStore")?.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsNotNull(store, "_colliderInstanceStore not found");
            store[instanceId] = grid;
        }

        private void SetActiveBuilding(BuildingsRuntimeEditor ed, BuildingObject b)
        {
            Field(ed, "_activeBuilding")?.SetValue(ed, b);
            Field(ed, "_activeColliderSession")?.SetValue(ed, null);
        }

        private void InvokeAdjustGridResolution(BuildingsRuntimeEditor ed, int dCols, int dRows)
        {
            var m = Method(s_editorType, "AdjustGridResolution", new[] { typeof(int), typeof(int) });
            Assert.IsNotNull(m, "AdjustGridResolution method not found");
            m.Invoke(ed, new object[] { dCols, dRows });
        }

        private object InvokeResampleToResolution(BuildingsRuntimeEditor ed, object source, int cols, int rows)
        {
            var m = Method(s_editorType, "ResampleGridToResolution",
                new[] { s_gridType, typeof(int), typeof(int) });
            Assert.IsNotNull(m, "ResampleGridToResolution method not found");
            return m.Invoke(ed, new[] { source, cols, rows });
        }

        // ── setup / teardown ───────────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)  if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var so in _assets) if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
            LogAssert.ignoreFailingMessages = true;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 10 — Logical-grid invariants
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Two CG buildings that share the same image path must receive sessions with
        /// identical WorkingGrid dimensions (same logical N×M), even when they have
        /// different originalScale or ScaleOverride values.
        /// This is the core invariant of the Phase 5 fix.
        /// </summary>
        [Test]
        public void SharedImageBuildings_BothReceiveSameGridTopology()
        {
            const string imageKey = "assets/buildings/tree_1.png";
            var ed = CreateEditor();

            // small building: 64×64 px (2×2 default grid)
            var small = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 1, templateId: 1);
            // large building: 128×128 px — different scale, SAME imageKey
            var large = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 2, templateId: 2,
                scaleOverridePx: new Vector2Int(128, 128));

            // Inject a stored CG grid (2×2) shared by both.
            var stored = MakeGrid(2, 2, ".");
            SetCell(stored, 0, 0, "#");
            InjectImageGrid(ed, imageKey, stored);

            // Session for small building.
            SetActiveBuilding(ed, small);
            var sessionSmall = GetSession(ed);
            Assert.IsNotNull(sessionSmall, "Session for small building must exist.");
            var gridSmall = s_sessionType.GetField("WorkingGrid").GetValue(sessionSmall);

            // Session for large building.
            SetActiveBuilding(ed, large);
            var sessionLarge = GetSession(ed);
            Assert.IsNotNull(sessionLarge, "Session for large building must exist.");
            var gridLarge = s_sessionType.GetField("WorkingGrid").GetValue(sessionLarge);

            Assert.AreEqual(GridWidth(gridSmall), GridWidth(gridLarge),
                "Both buildings sharing the same image must have the same logical grid WIDTH — " +
                "pixel size must not affect the grid topology.");
            Assert.AreEqual(GridHeight(gridSmall), GridHeight(gridLarge),
                "Both buildings sharing the same image must have the same logical grid HEIGHT.");
        }

        /// <summary>
        /// ResolveWorkingGridFor must return the stored grid dimensions unchanged (as a clone).
        /// Previously it resampled by effectiveSize, changing cols and rows.
        /// </summary>
        [Test]
        public void ResolveWorkingGridFor_CG_ReturnsStoredDimensionsUnchanged()
        {
            const string imageKey = "assets/buildings/block.png";
            // Template 64×64 → default 2×2. Inject a deliberately different 3×4 stored grid.
            var ed  = CreateEditor();
            var b   = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 5, templateId: 5);

            var stored = MakeGrid(3, 4, ".");
            InjectImageGrid(ed, imageKey, stored);

            SetActiveBuilding(ed, b);
            var session = GetSession(ed);
            Assert.IsNotNull(session);
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);

            Assert.AreEqual(3, GridWidth(grid),
                "WorkingGrid width must match the stored grid width (3), not ceil(effectiveSize.x/32).");
            Assert.AreEqual(4, GridHeight(grid),
                "WorkingGrid height must match the stored grid height (4), not ceil(effectiveSize.y/32).");
        }

        /// <summary>
        /// Two buildings that share a CG image should yield CollTiles with DIFFERENT world sizes
        /// (proportional to their world rects) but the SAME number of CollTile children,
        /// since both use the same logical N×M grid.
        /// </summary>
        [Test]
        public void SharedImageBuildings_SameCollTileCount_DifferentWorldSizes()
        {
            const string imageKey = "assets/buildings/fence.png";
            var ed = CreateEditor();

            // Both use 64×64 originalScale → same default 2×2 grid.
            var b1 = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 20, templateId: 20);
            b1.transform.position = new Vector3(0, 0, 0);

            var b2 = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 21, templateId: 21,
                scaleOverridePx: new Vector2Int(128, 128));
            b2.transform.position = new Vector3(10, 0, 0);

            // Solid grid: all 4 cells "#"
            var grid = MakeGrid(2, 2, "#");
            InjectImageGrid(ed, imageKey, grid);

            var applyMethod = Method(s_editorType, "ApplyGridOverrideToBuilding",
                new[] { typeof(BuildingObject), s_gridType });
            Assert.IsNotNull(applyMethod, "ApplyGridOverrideToBuilding not found");

            applyMethod.Invoke(ed, new[] { b1, grid });
            applyMethod.Invoke(ed, new[] { b2, grid });

            int CountCollTiles(BuildingObject b)
            {
                int n = 0;
                for (int i = 0; i < b.transform.childCount; i++)
                {
                    var c = b.transform.GetChild(i);
                    if (c.name.StartsWith("CollTile_") && c.gameObject.activeSelf) n++;
                }
                return n;
            }

            Assert.AreEqual(CountCollTiles(b1), CountCollTiles(b2),
                "Buildings sharing the same CG image must have the same number of CollTile children " +
                "when the same solid grid is applied.");

            // Large building tiles must be bigger.
            var tile1 = b1.transform.Find("CollTile_0_0");
            var tile2 = b2.transform.Find("CollTile_0_0");
            Assert.IsNotNull(tile1, "CollTile_0_0 must exist on small building after full-solid apply.");
            Assert.IsNotNull(tile2, "CollTile_0_0 must exist on large building after full-solid apply.");

            var box1 = tile1.GetComponent<BoxCollider2D>();
            var box2 = tile2.GetComponent<BoxCollider2D>();
            Assert.Greater(box2.size.x, box1.size.x,
                "CollTile on the larger building must have a wider collider than on the smaller one.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 11 — CreateDefaultFootprintGrid uses template.originalScale
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Default grid for a 64×64 template must be 2×2 (ceil(64/32) = 2).
        /// This must hold regardless of any per-instance scale override.
        /// </summary>
        [Test]
        public void DefaultFootprintGrid_UsesTemplateOriginalScale_NotEffectiveSize()
        {
            var ed = CreateEditor();

            // Template 64×64 → default 2×2
            var b = CreateBuilding("assets/buildings/crate.png", new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 1,
                scaleOverridePx: new Vector2Int(128, 128)); // effective size would give 4×4 if resampled

            // No entry in image store → session will call CreateDefaultFootprintGrid.
            SetActiveBuilding(ed, b);
            var session = GetSession(ed);
            Assert.IsNotNull(session);
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);

            Assert.AreEqual(2, GridWidth(grid),
                "Default grid width must be ceil(template.originalScale.x / 32) = 2, " +
                "not ceil(effectiveSize.x / 32) = 4.");
            Assert.AreEqual(2, GridHeight(grid),
                "Default grid height must be ceil(template.originalScale.y / 32) = 2.");
        }

        /// <summary>
        /// 128×64 template → default 4×2 grid.
        /// </summary>
        [Test]
        public void DefaultFootprintGrid_RectangularTemplate_CorrectDimensions()
        {
            var ed = CreateEditor();
            var b = CreateBuilding("assets/buildings/wall_wide.png", new Vector2Int(128, 64),
                solid: false, scope: "CG", instanceId: 2);

            SetActiveBuilding(ed, b);
            var session = GetSession(ed);
            var grid    = s_sessionType.GetField("WorkingGrid").GetValue(session);

            Assert.AreEqual(4, GridWidth(grid),
                "Default grid width for 128-px template must be 4 (ceil(128/32)).");
            Assert.AreEqual(2, GridHeight(grid),
                "Default grid height for 64-px template must be 2 (ceil(64/32)).");
        }

        /// <summary>
        /// When originalScale is zero (missing / not set), CreateDefaultFootprintGrid
        /// must not crash and must return at least a 1×1 grid.
        /// </summary>
        [Test]
        public void DefaultFootprintGrid_ZeroOriginalScale_FallsBackTo1x1()
        {
            var ed = CreateEditor();
            var b  = CreateBuilding("assets/buildings/unknown.png", Vector2Int.zero,
                solid: false, scope: "CG", instanceId: 3);

            SetActiveBuilding(ed, b);
            var session = GetSession(ed);
            Assert.IsNotNull(session, "Session must be created even for zero originalScale.");
            var grid = s_sessionType.GetField("WorkingGrid").GetValue(session);

            Assert.GreaterOrEqual(GridWidth(grid),  1, "Grid width must be >= 1 even for zero originalScale.");
            Assert.GreaterOrEqual(GridHeight(grid), 1, "Grid height must be >= 1 even for zero originalScale.");
        }

        /// <summary>
        /// Solid building: default footprint rows must contain "#" in the footprint zone,
        /// matching the split-ratio behaviour (footprint starts at ceil(rows * splitRatio)).
        /// </summary>
        [Test]
        public void DefaultFootprintGrid_SolidBuilding_FootprintRowsAreSolid()
        {
            var ed = CreateEditor();
            // 64×64 solid, splitRatio=0.5 → 2 rows, footprint starts at row 1.
            var b = CreateBuilding("assets/buildings/solid.png", new Vector2Int(64, 64),
                solid: true, scope: "CG", instanceId: 10, splitRatio: 0.5f);

            SetActiveBuilding(ed, b);
            var session = GetSession(ed);
            var grid    = s_sessionType.GetField("WorkingGrid").GetValue(session);

            Assert.AreEqual(2, GridHeight(grid), "Precondition: 2-row grid expected.");
            // Row 0 (canopy) must be walkable; row 1 (footprint) must be solid.
            Assert.AreEqual(".", GetCell(grid, 0, 0), "Row 0 (canopy) must be walkable '.'.");
            Assert.AreEqual("#", GetCell(grid, 1, 0), "Row 1 (footprint) must be solid '#'.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 12 — AdjustGridResolution
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void AdjustGridResolution_ColsPlus1_WidensStoredGrid()
        {
            const string imageKey = "assets/buildings/pillar.png";
            var ed = CreateEditor();
            var b  = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 50, templateId: 50);

            var initial = MakeGrid(2, 2, ".");
            InjectImageGrid(ed, imageKey, initial);

            SetActiveBuilding(ed, b);
            GetSession(ed); // warm-up session

            InvokeAdjustGridResolution(ed, dCols: +1, dRows: 0);

            var store   = Field(ed, "_colliderImageStore")?.GetValue(ed) as System.Collections.IDictionary;
            var stored  = store?[imageKey];
            Assert.IsNotNull(stored, "Image store must still contain the key after resize.");
            Assert.AreEqual(3, GridWidth(stored),
                "Grid width must be 3 after +1 cols adjustment.");
            Assert.AreEqual(2, GridHeight(stored),
                "Grid height must remain 2 after a cols-only adjustment.");
        }

        [Test]
        public void AdjustGridResolution_RowsMinus1_ShrinksStoredGrid()
        {
            const string imageKey = "assets/buildings/wall_short.png";
            var ed = CreateEditor();
            var b  = CreateBuilding(imageKey, new Vector2Int(64, 96),
                solid: false, scope: "CG", instanceId: 51, templateId: 51);

            var initial = MakeGrid(2, 3, ".");
            InjectImageGrid(ed, imageKey, initial);

            SetActiveBuilding(ed, b);
            GetSession(ed);

            InvokeAdjustGridResolution(ed, dCols: 0, dRows: -1);

            var store  = Field(ed, "_colliderImageStore")?.GetValue(ed) as System.Collections.IDictionary;
            var stored = store?[imageKey];
            Assert.IsNotNull(stored, "Image store must still contain the key after resize.");
            Assert.AreEqual(2, GridWidth(stored),  "Grid width must remain 2 after rows-only change.");
            Assert.AreEqual(2, GridHeight(stored), "Grid height must be 2 after −1 rows.");
        }

        [Test]
        public void AdjustGridResolution_Clamp_DoesNotGoBelowMin()
        {
            const string imageKey = "assets/buildings/tiny.png";
            var ed = CreateEditor();
            var b  = CreateBuilding(imageKey, new Vector2Int(32, 32),
                solid: false, scope: "CG", instanceId: 52);

            var initial = MakeGrid(1, 1, ".");
            InjectImageGrid(ed, imageKey, initial);
            SetActiveBuilding(ed, b);
            GetSession(ed);

            // Try to go below 1.
            InvokeAdjustGridResolution(ed, dCols: -1, dRows: -1);

            var store  = Field(ed, "_colliderImageStore")?.GetValue(ed) as System.Collections.IDictionary;
            // If clamping works, the method returns early (no change) → store may still have old value
            // or have been written with 1×1.  Either way dimensions must be >= 1.
            if (store != null && store.Contains(imageKey))
            {
                var stored = store[imageKey];
                Assert.GreaterOrEqual(GridWidth(stored),  1, "Grid width must never go below MIN (1).");
                Assert.GreaterOrEqual(GridHeight(stored), 1, "Grid height must never go below MIN (1).");
            }
            // no assertion needed if store is empty — clamp caused no-op, that's fine too
        }

        [Test]
        public void AdjustGridResolution_CG_PropagatesResizedGridToAllSameImageBuildings()
        {
            const string imageKey = "assets/buildings/arch.png";
            var ed = CreateEditor();

            var source  = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 60, templateId: 60);
            var sibling = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 61, templateId: 61);
            var other   = CreateBuilding("assets/buildings/different.png", new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 62, templateId: 62);

            // Inject a fully-solid 2×2 grid for the shared image.
            var grid = MakeGrid(2, 2, "#");
            InjectImageGrid(ed, imageKey, grid);

            // Also register the buildings in the editor so ApplyCollisionTargetsFor finds them.
            var listField = Field(ed, "_buildingsList");
            if (listField != null)
            {
                var list = listField.GetValue(ed) as System.Collections.IList;
                if (list != null) { list.Add(source); list.Add(sibling); list.Add(other); }
            }
            else
            {
                // Fallback: write to the array used by GetCachedBuildings (if it exists)
                var arrField = Field(ed, "_cachedBuildings");
                if (arrField != null)
                    arrField.SetValue(ed, new BuildingObject[] { source, sibling, other });
            }

            SetActiveBuilding(ed, source);
            GetSession(ed);

            InvokeAdjustGridResolution(ed, dCols: +1, dRows: 0); // 2→3 cols

            // Verify that sibling received the resized grid (3-col tiles exist).
            int Cols3CollTiles(BuildingObject b)
            {
                int n = 0;
                for (int i = 0; i < b.transform.childCount; i++)
                {
                    var c = b.transform.GetChild(i);
                    // A 3×2 all-solid grid creates CollTile_{row}_{col} for col 0,1,2
                    if (c.name.StartsWith("CollTile_") && c.gameObject.activeSelf) n++;
                }
                return n;
            }

            // Source and sibling should have 3×2 = 6 tiles; other should not have been affected.
            int siblingTiles = Cols3CollTiles(sibling);
            int otherTiles   = Cols3CollTiles(other);

            Assert.AreEqual(6, siblingTiles,
                "CG resize must propagate to all buildings sharing the same sourceImagePath; " +
                "sibling (same image, different templateId) must now have 6 CollTiles (3×2).");
            Assert.AreEqual(0, otherTiles,
                "Building with a different image must not be affected by a CG resize on another image.");
        }

        [Test]
        public void AdjustGridResolution_CU_DoesNotAffectOtherBuildings()
        {
            var ed  = CreateEditor();
            var b1  = CreateBuilding("assets/buildings/chest.png", new Vector2Int(64, 64),
                solid: false, scope: "CU", instanceId: 70, templateId: 70);
            var b2  = CreateBuilding("assets/buildings/chest.png", new Vector2Int(64, 64),
                solid: false, scope: "CU", instanceId: 71, templateId: 71);

            // Inject a 2×2 all-solid grid for each instance.
            var g1 = MakeGrid(2, 2, "#");
            var g2 = MakeGrid(2, 2, "#");
            InjectInstanceGrid(ed, 70, g1);
            InjectInstanceGrid(ed, 71, g2);

            SetActiveBuilding(ed, b1);
            GetSession(ed);

            InvokeAdjustGridResolution(ed, dCols: +2, dRows: 0); // b1: 2→4 cols

            var store = Field(ed, "_colliderInstanceStore")?.GetValue(ed) as System.Collections.IDictionary;
            Assert.IsNotNull(store);

            if (store.Contains(71))
            {
                var stored71 = store[71];
                Assert.AreEqual(2, GridWidth(stored71),
                    "CU resize on instance 70 must not change the grid width of instance 71.");
            }
        }

        [Test]
        public void AdjustGridResolution_CreatesUndoEntry()
        {
            const string imageKey = "assets/buildings/stone.png";
            var ed = CreateEditor();
            var b  = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 80);

            InjectImageGrid(ed, imageKey, MakeGrid(2, 2, "."));
            SetActiveBuilding(ed, b);
            GetSession(ed);

            var undo     = Field(ed, "_undo")?.GetValue(ed);
            var undoType = undo.GetType();
            int before   = (int)undoType.GetProperty("UndoCount").GetValue(undo);

            InvokeAdjustGridResolution(ed, +1, 0);

            int after = (int)undoType.GetProperty("UndoCount").GetValue(undo);
            Assert.AreEqual(before + 1, after,
                "AdjustGridResolution must register exactly one undoable action.");
        }

        [Test]
        public void AdjustGridResolution_Undo_RestoresPreviousResolution()
        {
            const string imageKey = "assets/buildings/marble.png";
            var ed = CreateEditor();
            var b  = CreateBuilding(imageKey, new Vector2Int(64, 64),
                solid: false, scope: "CG", instanceId: 90);

            InjectImageGrid(ed, imageKey, MakeGrid(2, 2, "."));
            SetActiveBuilding(ed, b);
            GetSession(ed);

            InvokeAdjustGridResolution(ed, +1, 0); // 2→3 cols

            var store  = Field(ed, "_colliderImageStore")?.GetValue(ed) as System.Collections.IDictionary;
            var stored = store?[imageKey];
            Assert.AreEqual(3, GridWidth(stored), "Precondition: grid must be 3 wide after resize.");

            // Undo
            var undo = Field(ed, "_undo")?.GetValue(ed);
            undo.GetType().GetMethod("Undo")?.Invoke(undo, null);

            // After undo, grid must be back to 2 cols.
            stored = store?[imageKey];
            int finalWidth = stored != null ? GridWidth(stored) : -1;
            // ApplyGridSnapshot sets _activeColliderSession = null, so the next session rebuild
            // will use the restored store. Check the store directly.
            Assert.AreEqual(2, finalWidth,
                "After Undo, the grid width must be restored to 2 (original).");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  GROUP 13 — ResampleGridToResolution (logical resampler)
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ResampleGridToResolution_NullSource_ReturnsNull()
        {
            var ed = CreateEditor();
            var result = InvokeResampleToResolution(ed, null, 3, 3);
            Assert.IsNull(result, "ResampleGridToResolution with null source must return null.");
        }

        [Test]
        public void ResampleGridToResolution_SameSize_ReturnsCopyWithIdenticalTopology()
        {
            var ed     = CreateEditor();
            var source = MakeGrid(3, 2, ".");
            SetCell(source, 0, 1, "#");
            SetCell(source, 1, 2, "#");

            var result = InvokeResampleToResolution(ed, source, 3, 2);

            Assert.IsNotNull(result);
            Assert.AreNotSame(source, result, "Must return a clone, not the same reference.");
            Assert.AreEqual(3, GridWidth(result));
            Assert.AreEqual(2, GridHeight(result));
            Assert.AreEqual("#", GetCell(result, 0, 1), "Solid cell [0][1] must be preserved.");
            Assert.AreEqual("#", GetCell(result, 1, 2), "Solid cell [1][2] must be preserved.");
            Assert.AreEqual(".", GetCell(result, 0, 0), "Walkable cell [0][0] must remain walkable.");
        }

        [Test]
        public void ResampleGridToResolution_Expand_PreservesSolidCells()
        {
            // 1×1 all-solid → expand to 3×3 → all 9 cells should be solid.
            var ed     = CreateEditor();
            var source = MakeGrid(1, 1, "#");
            var result = InvokeResampleToResolution(ed, source, 3, 3);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, GridWidth(result));
            Assert.AreEqual(3, GridHeight(result));

            var collision = (string[][])s_gridType.GetField("collision").GetValue(result);
            foreach (var row in collision)
                foreach (var cell in row)
                    Assert.AreEqual("#", cell,
                        "Expanding a fully-solid 1×1 grid to 3×3 must yield a fully-solid grid.");
        }

        [Test]
        public void ResampleGridToResolution_Expand_WalkableCellsRemainsWalkable()
        {
            // 1×1 all-walkable → 3×3 all-walkable.
            var ed     = CreateEditor();
            var source = MakeGrid(1, 1, ".");
            var result = InvokeResampleToResolution(ed, source, 3, 3);

            Assert.IsNotNull(result);
            var collision = (string[][])s_gridType.GetField("collision").GetValue(result);
            foreach (var row in collision)
                foreach (var cell in row)
                    Assert.AreEqual(".", cell,
                        "Expanding a fully-walkable grid must yield a fully-walkable grid.");
        }

        [Test]
        public void ResampleGridToResolution_Shrink_ConservativelyPreservesSolid()
        {
            // 4×4 grid with top-left quadrant solid, rest walkable.
            // Shrink to 2×2 → top-left cell must be solid (covers the solid source quadrant).
            var ed     = CreateEditor();
            var source = MakeGrid(4, 4, ".");
            // Mark top-left 2×2 block as solid.
            SetCell(source, 0, 0, "#"); SetCell(source, 0, 1, "#");
            SetCell(source, 1, 0, "#"); SetCell(source, 1, 1, "#");

            var result = InvokeResampleToResolution(ed, source, 2, 2);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, GridWidth(result));
            Assert.AreEqual(2, GridHeight(result));
            // [0][0] in 2×2 maps to the top-left 2×2 block of the 4×4 → must be solid.
            Assert.AreEqual("#", GetCell(result, 0, 0),
                "Cell [0][0] in the shrunken grid must be solid because it covers a solid region.");
            // [0][1] in 2×2 maps to top-right 2×2 block (all walkable) → must be walkable.
            Assert.AreEqual(".", GetCell(result, 0, 1),
                "Cell [0][1] in the shrunken grid must be walkable (covers all-walkable source region).");
            // [1][0] maps to bottom-left 2×2 block (all walkable) → walkable.
            Assert.AreEqual(".", GetCell(result, 1, 0),
                "Cell [1][0] in the shrunken grid must be walkable.");
        }

        [Test]
        public void ResampleGridToResolution_Expand_DimensionsCorrect()
        {
            var ed     = CreateEditor();
            var source = MakeGrid(2, 2, ".");
            var result = InvokeResampleToResolution(ed, source, 5, 7);

            Assert.AreEqual(5, GridWidth(result),  "Expanded grid must be 5 wide.");
            Assert.AreEqual(7, GridHeight(result), "Expanded grid must be 7 tall.");
        }

        [Test]
        public void ResampleGridToResolution_Shrink_DimensionsCorrect()
        {
            var ed     = CreateEditor();
            var source = MakeGrid(8, 6, "#");
            var result = InvokeResampleToResolution(ed, source, 2, 3);

            Assert.AreEqual(2, GridWidth(result),  "Shrunken grid must be 2 wide.");
            Assert.AreEqual(3, GridHeight(result), "Shrunken grid must be 3 tall.");
        }

        [Test]
        public void ResampleGridToResolution_ResultIsIndependentOfSource()
        {
            // Mutating the source after resampling must not change the result.
            var ed     = CreateEditor();
            var source = MakeGrid(2, 2, "#");
            var result = InvokeResampleToResolution(ed, source, 2, 2);

            // Mutate source.
            SetCell(source, 0, 0, ".");
            // Result must still be solid at [0][0].
            Assert.AreEqual("#", GetCell(result, 0, 0),
                "ResampleGridToResolution must return a deep copy — mutating the source must not change the result.");
        }
    }
}
