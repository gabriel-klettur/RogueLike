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
    /// <summary>ColliderBrushTests: the grid override tests. SetUp, TearDown and helpers live in ColliderBrushTests.cs.</summary>
    public partial class ColliderBrushTests
    {
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
            string scope = "CG", int instanceId = 1, int templateId = 1)
        {
            var ed       = CreateEditor();
            var building = CreateSolidBuilding(solid: solidBuilding, scope: scope, instanceId: instanceId, templateId: templateId);

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
    }
}
