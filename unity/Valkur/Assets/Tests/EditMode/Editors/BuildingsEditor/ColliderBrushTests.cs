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
    ///   • CG uses template shared key; CU uses instanceId; session is cached.
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
    public partial class ColliderBrushTests
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
                                                   bool solid = true, string scope = "CG", int instanceId = 1,
                                                   int templateId = 1)
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.templateId      = templateId;
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

        private static string FlattenGrid(object grid)
        {
            var collision = (string[][])s_gridType.GetField("collision").GetValue(grid);
            var rows = new List<string>(collision.Length);
            foreach (var row in collision)
                rows.Add(string.Join("", row));
            return string.Join("|", rows);
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

    }
}
