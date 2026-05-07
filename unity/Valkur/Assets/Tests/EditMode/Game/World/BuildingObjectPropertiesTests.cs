using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Unit tests for the properties and helpers added to <see cref="BuildingObject"/>
    /// during the Buildings Editor migration (Gaps 1, 7, 8):
    ///   - ZBottomOffset / ZTopOffset  (Gap 7 – Z-layer inspector)
    ///   - ColliderScopeOverride / EffectiveColliderScope (Gap 8 – scope toggle)
    ///   - TryGetWorldRect(out Rect)   (Gap 1 – hover hit-test + outline anchor)
    ///
    /// Python reference: roguelike_editors/buildings/building_editor_view.py
    ///   building.z_bottom / building.z_top    → ZBottomOffset / ZTopOffset
    ///   building.collider_scope               → EffectiveColliderScope
    ///   pygame.Rect(building.rect)            → TryGetWorldRect
    /// </summary>
    [TestFixture]
    public class BuildingObjectPropertiesTests
    {
        // ── helpers ─────────────────────────────────────────────────────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetPrivateField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        /// <summary>Creates a 1×1-pixel white sprite with PPU=32 and a given size.</summary>
        private static Sprite MakeSprite(int texWidth, int texHeight, float ppu = 32f)
        {
            var tex = new Texture2D(texWidth, texHeight);
            tex.SetPixels(new Color[texWidth * texHeight]);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, texWidth, texHeight), new Vector2(0.5f, 0f), ppu);
        }

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        // ── Z-Bottom / Z-Top properties ──────────────────────────────────────

        [Test]
        public void ZBottomOffset_GetSet_StoresValue()
        {
            // Suppress renderer-material leak warnings in EditMode.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            bObj.ZBottomOffset = 7;

            Assert.AreEqual(7, bObj.ZBottomOffset,
                "ZBottomOffset should store the written value.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ZTopOffset_GetSet_StoresValue()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            bObj.ZTopOffset = -3;

            Assert.AreEqual(-3, bObj.ZTopOffset,
                "ZTopOffset should store the written value including negatives.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ZBottomOffset_Default_IsZero()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            Assert.AreEqual(0, bObj.ZBottomOffset, "ZBottomOffset default must be 0 (maps to Python z_bottom=0).");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ZTopOffset_Default_IsZero()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            Assert.AreEqual(0, bObj.ZTopOffset, "ZTopOffset default must be 0 (maps to Python z_top=0).");

            Object.DestroyImmediate(go);
        }

        // ── ColliderScopeOverride / EffectiveColliderScope ────────────────────

        [Test]
        public void ColliderScopeOverride_GetSet_StoresValue()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            bObj.ColliderScopeOverride = "CU";

            Assert.AreEqual("CU", bObj.ColliderScopeOverride,
                "ColliderScopeOverride should round-trip correctly.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ColliderScopeOverride_SetNull_StoresEmptyString()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            bObj.ColliderScopeOverride = null;

            // Setter: value ?? "" — null is coerced to empty string.
            Assert.AreEqual("", bObj.ColliderScopeOverride,
                "Setting ColliderScopeOverride to null must store empty string, not throw.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void EffectiveColliderScope_NoTemplate_ReturnsCG()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();
            // _template is null by default, no override set.

            string scope = bObj.EffectiveColliderScope;

            Assert.AreEqual("CG", scope,
                "With no template and no override, EffectiveColliderScope must fall back to 'CG' " +
                "(Python default collider_scope).");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void EffectiveColliderScope_ReturnsTemplateScope_WhenNoOverride()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.colliderScope = "CU";
            SetPrivateField(bObj, "_template", tmpl);

            string scope = bObj.EffectiveColliderScope;

            Assert.AreEqual("CU", scope,
                "EffectiveColliderScope should return template.colliderScope when no per-instance override is set.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
        }

        [Test]
        public void EffectiveColliderScope_OverrideWins_OverTemplateScope()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.colliderScope = "CG";
            SetPrivateField(bObj, "_template", tmpl);
            bObj.ColliderScopeOverride = "CU";

            string scope = bObj.EffectiveColliderScope;

            Assert.AreEqual("CU", scope,
                "Per-instance ColliderScopeOverride must take precedence over template.colliderScope.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
        }

        // ── TryGetWorldRect ─────────────────────────────────────────────────

        [Test]
        public void TryGetWorldRect_NoBottomRenderer_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();
            // _bottomRenderer is null — Apply() was never called.

            bool result = bObj.TryGetWorldRect(out _);

            Assert.IsFalse(result,
                "TryGetWorldRect must return false when no bottom renderer has been created yet.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldRect_BottomRendererNoSprite_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            // Inject a SpriteRenderer with no sprite assigned.
            var childGo = new GameObject("Footprint");
            childGo.transform.SetParent(go.transform);
            var sr = childGo.AddComponent<SpriteRenderer>();
            sr.sprite = null;
            SetPrivateField(bObj, "_bottomRenderer", sr);

            bool result = bObj.TryGetWorldRect(out _);

            Assert.IsFalse(result,
                "TryGetWorldRect must return false when bottom renderer has no sprite.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldRect_WithBottomRenderer_ReturnsCorrectRect()
        {
            // Texture 64×64, PPU=32 → world size 2×2 (bottom only, no top).
            // Building at (5, 3, 0), scale (1,1,1).
            // Expected: Rect(x=4, y=3, w=2, h=2)
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(5f, 3f, 0f);
            var bObj = go.AddComponent<BuildingObject>();

            var childGo = new GameObject("Footprint");
            childGo.transform.SetParent(go.transform);
            var sr = childGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(64, 64, 32f);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            bool result = bObj.TryGetWorldRect(out Rect rect);

            Assert.IsTrue(result, "TryGetWorldRect should succeed with a valid bottom renderer.");
            Assert.AreEqual(4f, rect.x,      0.001f, "rect.xMin = pos.x − width/2");
            Assert.AreEqual(3f, rect.y,      0.001f, "rect.yMin = pos.y (bottom anchor)");
            Assert.AreEqual(2f, rect.width,  0.001f, "rect.width = 64px / 32PPU = 2");
            Assert.AreEqual(2f, rect.height, 0.001f, "rect.height = 64px / 32PPU = 2 (bottom only)");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldRect_WithBothRenderers_AccumulatesHeight()
        {
            // Bottom 64×64 → height 2; Top 64×32 → height 1. Total height = 3.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(0f, 0f, 0f);
            var bObj = go.AddComponent<BuildingObject>();

            var bottomGo = new GameObject("Footprint");
            bottomGo.transform.SetParent(go.transform);
            var bottomSr = bottomGo.AddComponent<SpriteRenderer>();
            bottomSr.sprite = MakeSprite(64, 64, 32f);
            SetPrivateField(bObj, "_bottomRenderer", bottomSr);

            var topGo = new GameObject("Canopy");
            topGo.transform.SetParent(go.transform);
            var topSr = topGo.AddComponent<SpriteRenderer>();
            topSr.sprite = MakeSprite(64, 32, 32f);
            SetPrivateField(bObj, "_topRenderer", topSr);

            bool result = bObj.TryGetWorldRect(out Rect rect);

            Assert.IsTrue(result, "TryGetWorldRect should succeed with both renderers.");
            Assert.AreEqual(3f, rect.height, 0.001f,
                "Total height = bottomH(2) + topH(1) = 3. Maps to Python's full sprite rect height.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldRect_LocalScale_AffectsWorldSize()
        {
            // Texture 64×64, PPU=32. Scale = (2, 3, 1) → world size 4×6.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.transform.localScale = new Vector3(2f, 3f, 1f);
            var bObj = go.AddComponent<BuildingObject>();

            var childGo = new GameObject("Footprint");
            childGo.transform.SetParent(go.transform);
            var sr = childGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(64, 64, 32f);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            bool result = bObj.TryGetWorldRect(out Rect rect);

            Assert.IsTrue(result);
            Assert.AreEqual(4f, rect.width,  0.001f, "Width scales with localScale.x: 2 * 2 = 4");
            Assert.AreEqual(6f, rect.height, 0.001f, "Height scales with localScale.y: 2 * 3 = 6");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldRect_Contains_WorldPositionInsideBuilding()
        {
            // Building at (5, 3), world size 2×2 → rect covers x=[4,6], y=[3,5].
            // A point at (5, 4) should be inside.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(5f, 3f, 0f);
            var bObj = go.AddComponent<BuildingObject>();

            var childGo = new GameObject("Footprint");
            childGo.transform.SetParent(go.transform);
            var sr = childGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(64, 64, 32f);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            bObj.TryGetWorldRect(out Rect rect);
            bool inside  = rect.Contains(new Vector2(5f, 4f));
            bool outside = rect.Contains(new Vector2(7f, 4f));

            Assert.IsTrue(inside,  "Point (5,4) should be inside the building rect.");
            Assert.IsFalse(outside, "Point (7,4) should be outside the building rect.");

            Object.DestroyImmediate(go);
        }

        // ── TryGetWorldCellRect ─────────────────────────────────────────────
        // Single source of truth: visual overlay, click-to-paint and physical
        // BoxCollider2D placement all read cells from this helper. Drift here
        // means colliders end up where the visual ISN'T, and the player walks
        // through what looks like a wall.

        [Test]
        public void TryGetWorldCellRect_InvalidGridDimensions_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();
            var sr = MakeChildRenderer(go, "Footprint", 64, 64);
            SetPrivateField(bObj, "_bottomRenderer", sr);

            Assert.IsFalse(bObj.TryGetWorldCellRect(0, 0, 0, 1, out _),
                "rows == 0 must return false.");
            Assert.IsFalse(bObj.TryGetWorldCellRect(0, 0, 1, 0, out _),
                "cols == 0 must return false.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldCellRect_NoBottomRenderer_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();

            Assert.IsFalse(bObj.TryGetWorldCellRect(0, 0, 1, 1, out _),
                "Must return false when TryGetWorldRect itself fails.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldCellRect_SingleCell_EqualsFullWorldRect()
        {
            // 1×1 grid → the only cell must equal the full world rect.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(10f, 20f, 0f);
            var bObj = go.AddComponent<BuildingObject>();
            var sr = MakeChildRenderer(go, "Footprint", 64, 64); // 2×2 world units
            SetPrivateField(bObj, "_bottomRenderer", sr);

            Assert.IsTrue(bObj.TryGetWorldRect(out var full));
            Assert.IsTrue(bObj.TryGetWorldCellRect(0, 0, 1, 1, out var cell));

            Assert.AreEqual(full.xMin,   cell.xMin,   0.001f);
            Assert.AreEqual(full.yMin,   cell.yMin,   0.001f);
            Assert.AreEqual(full.width,  cell.width,  0.001f);
            Assert.AreEqual(full.height, cell.height, 0.001f);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldCellRect_Row0_IsTopOfBuilding()
        {
            // Row 0 must map to the TOP of the building (highest yMin).
            // This is the contract HandleColliderPaint relies on:
            // row = floor((1 - v) * rows). v=1 (top) → row 0.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = Vector3.zero;
            var bObj = go.AddComponent<BuildingObject>();
            var sr = MakeChildRenderer(go, "Footprint", 64, 128); // 2 wide × 4 tall
            SetPrivateField(bObj, "_bottomRenderer", sr);

            // 4 rows → cell height = 1
            Assert.IsTrue(bObj.TryGetWorldCellRect(0, 0, 4, 1, out var top));
            Assert.IsTrue(bObj.TryGetWorldCellRect(3, 0, 4, 1, out var bottom));

            Assert.Greater(top.yMin, bottom.yMin,
                "Row 0 must be ABOVE row 3 (row 0 = top of building, row N-1 = bottom).");
            Assert.AreEqual(0f, bottom.yMin, 0.001f, "Row N-1 sits on the ground anchor.");
            Assert.AreEqual(3f, top.yMin,    0.001f, "Row 0 sits at (rows-1) cell heights above the ground.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGetWorldCellRect_TilesCoverFullRect_NoGapsNoOverlap()
        {
            // Iterating every cell of an N×M grid must reproduce exactly the
            // full world rect — no gaps, no overlap, no rounding drift.
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TestBuilding");
            go.transform.position = new Vector3(7f, 13f, 0f);
            var bObj = go.AddComponent<BuildingObject>();
            var sr = MakeChildRenderer(go, "Footprint", 96, 64); // 3 wide × 2 tall
            SetPrivateField(bObj, "_bottomRenderer", sr);

            int rows = 4, cols = 6;
            Assert.IsTrue(bObj.TryGetWorldRect(out var full));

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            float totalArea = 0f;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                Assert.IsTrue(bObj.TryGetWorldCellRect(r, c, rows, cols, out var cell));
                minX = Mathf.Min(minX, cell.xMin); minY = Mathf.Min(minY, cell.yMin);
                maxX = Mathf.Max(maxX, cell.xMax); maxY = Mathf.Max(maxY, cell.yMax);
                totalArea += cell.width * cell.height;
            }

            Assert.AreEqual(full.xMin, minX, 0.001f, "Cells must start at building xMin.");
            Assert.AreEqual(full.yMin, minY, 0.001f, "Cells must start at building yMin.");
            Assert.AreEqual(full.xMax, maxX, 0.001f, "Cells must end at building xMax.");
            Assert.AreEqual(full.yMax, maxY, 0.001f, "Cells must end at building yMax.");
            Assert.AreEqual(full.width * full.height, totalArea, 0.001f,
                "Sum of cell areas must equal full rect area (no gaps, no overlap).");

            Object.DestroyImmediate(go);
        }

        // ── RefreshSorting (Z-sort drift after position change) ────────────
        // Regression for the drag-move bug: BuildingsRuntimeEditor mutates
        // transform.position each frame during a right-mouse drag. Without
        // a manual RefreshSorting() call the bottom + top renderers keep
        // the sortingOrder they got at Apply()-time, so the building stops
        // Y-sorting against entities once it moves. These tests lock the
        // contract: RefreshSorting() must rewrite both children's
        // sortingOrders from the CURRENT transform.position.y.

        [Test]
        public void RefreshSorting_AfterPositionChange_RewritesBothRendererSortingOrders()
        {
            LogAssert.ignoreFailingMessages = true;

            var go    = new GameObject("TestBuilding");
            var bObj  = go.AddComponent<BuildingObject>();
            var bottom = MakeChildRenderer(go, "Footprint", 64, 64);
            var top    = MakeChildRenderer(go, "Canopy",    64, 32);
            SetPrivateField(bObj, "_bottomRenderer", bottom);
            SetPrivateField(bObj, "_topRenderer",    top);

            // Initial position + first sort.
            go.transform.position = new Vector3(0f, 0f, 0f);
            bObj.RefreshSorting();
            int initialBottom = bottom.sortingOrder;
            int initialTop    = top.sortingOrder;

            // Move 5 world units up.
            go.transform.position = new Vector3(0f, 5f, 0f);
            bObj.RefreshSorting();

            // YToSortingOrder = -(y * 100), so a +5 Y move must subtract 500.
            Assert.AreEqual(initialBottom - 500, bottom.sortingOrder,
                "Bottom renderer sortingOrder must reflect the new transform.position.y after RefreshSorting().");
            Assert.AreEqual(initialTop - 500, top.sortingOrder,
                "Top renderer sortingOrder must reflect the new transform.position.y after RefreshSorting().");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RefreshSorting_PreservesPerInstanceZOffsets()
        {
            LogAssert.ignoreFailingMessages = true;

            var go    = new GameObject("TestBuilding");
            var bObj  = go.AddComponent<BuildingObject>();
            var bottom = MakeChildRenderer(go, "Footprint", 64, 64);
            var top    = MakeChildRenderer(go, "Canopy",    64, 32);
            SetPrivateField(bObj, "_bottomRenderer", bottom);
            SetPrivateField(bObj, "_topRenderer",    top);

            bObj.ZBottomOffset = 3;
            bObj.ZTopOffset    = -7;

            go.transform.position = new Vector3(0f, 2f, 0f);
            bObj.RefreshSorting();

            // -(2 * 100) + 3   for bottom
            // -(2 * 100) + (-7) for top
            Assert.AreEqual(-200 + 3, bottom.sortingOrder,
                "RefreshSorting() must keep the per-instance ZBottomOffset.");
            Assert.AreEqual(-200 - 7, top.sortingOrder,
                "RefreshSorting() must keep the per-instance ZTopOffset.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RefreshSorting_NoRenderers_DoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;

            var go   = new GameObject("TestBuilding");
            var bObj = go.AddComponent<BuildingObject>();
            // No bottom/top renderers wired — simulates a building whose
            // Apply() failed before reaching the renderer-creation step.

            Assert.DoesNotThrow(() => bObj.RefreshSorting(),
                "RefreshSorting() must be safe to call even when renderers haven't been built yet.");

            Object.DestroyImmediate(go);
        }

        // ── Small helper to keep the new tests compact ─────────────────────
        private static SpriteRenderer MakeChildRenderer(GameObject parent, string name, int texW, int texH)
        {
            var childGo = new GameObject(name);
            childGo.transform.SetParent(parent.transform);
            var sr = childGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSprite(texW, texH, 32f);
            return sr;
        }
    }
}
