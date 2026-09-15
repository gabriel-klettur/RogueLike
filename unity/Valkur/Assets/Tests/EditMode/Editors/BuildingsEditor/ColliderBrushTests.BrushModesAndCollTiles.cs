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
    /// <summary>ColliderBrushTests: the brush modes and coll tiles tests. SetUp, TearDown and helpers live in ColliderBrushTests.cs.</summary>
    public partial class ColliderBrushTests
    {
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
            const int templateId = 456;
            const string expectedKey = "assets/buildings/wall_cg.png";
            var (ed, building)  = SetupForPaint("Walk", solidBuilding: true, scope: "CG", templateId: templateId);
            var templateField   = typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var template = (BuildingTemplateData)templateField.GetValue(building);
            template.sourceImagePath = "assets/buildings/wall_cg.png";

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var paintMethod = Method(s_editorType, "HandleColliderPaint",  new[] { typeof(Vector3) });
            var endMethod   = Method(s_editorType, "EndColliderStroke",    Type.EmptyTypes);

            beginMethod.Invoke(ed, null);
            paintMethod.Invoke(ed, new object[] { new Vector3(0f, 1f, 0f) });
            endMethod.Invoke(ed, null);

            var store = Field(ed, "_colliderImageStore")?.GetValue(ed)
                            as System.Collections.IDictionary;
            Assert.IsNotNull(store);
            Assert.IsTrue(store.Contains(expectedKey),
                "Image store must contain the template shared key after CG stroke end.");
        }

        [Test]
        public void EndColliderStroke_CG_SharedAppliesToSameImagePathOnly()
        {
            var ed = CreateEditor();
            // source and siblingDifferentTemplateId share the same image → must both receive the authoured colliders.
            var source = CreateSolidBuilding(imageKey: "assets/buildings/test.png",  solid: false, scope: "CG", instanceId: 10, templateId: 700);
            var siblingDifferentTemplateId = CreateSolidBuilding(imageKey: "assets/buildings/test.png",  solid: false, scope: "CG", instanceId: 11, templateId: 701);
            // Different image path: must NOT receive source colliders.
            var differentImage = CreateSolidBuilding(imageKey: "assets/buildings/other_asset.png", solid: false, scope: "CG", instanceId: 12, templateId: 702);

            Field(ed, "_activeBuilding")?.SetValue(ed, source);
            var modeField = Field(ed, "_collBrushMode");
            modeField.SetValue(ed, Enum.Parse(modeField.FieldType, "Solid"));

            var beginMethod = Method(s_editorType, "BeginColliderStroke", Type.EmptyTypes);
            var paintMethod = Method(s_editorType, "HandleColliderPaint",  new[] { typeof(Vector3) });
            var endMethod   = Method(s_editorType, "EndColliderStroke",    Type.EmptyTypes);

            beginMethod.Invoke(ed, null);
            paintMethod.Invoke(ed, new object[] { new Vector3(0f, 1f, 0f) });
            endMethod.Invoke(ed, null);

            Assert.IsNotNull(siblingDifferentTemplateId.transform.Find("CollTile_1_1"),
                "CG edit must propagate to buildings sharing the same source image path, regardless of templateId.");
            Assert.IsNull(differentImage.transform.Find("CollTile_1_1"),
                "CG edit must not propagate to buildings with a different source image path.");
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
