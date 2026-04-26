using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Unit tests for <see cref="BuildingOutlineRenderer"/> — the world-space
    /// LineRenderer + SpriteRenderer highlight system introduced for Gap 1
    /// (hover cyan outline / active yellow outline / remove red fill).
    ///
    /// Python reference: roguelike_editors/buildings/building_editor_view.py
    ///   hover (no remove): pygame.draw.rect(surf, (0,255,255), rect, 2)
    ///   hover (remove):    pygame.draw.rect(surf, (255,0,0), rect, 3) + fill alpha 60
    ///   active:            pygame.draw.rect(surf, (255,215,0), rect, 5)
    /// </summary>
    [TestFixture]
    public class BuildingOutlineRendererTests
    {
        private GameObject _go;
        private BuildingOutlineRenderer _outline;

        [SetUp]
        public void SetUp()
        {
            // Suppress renderer.material leak warnings which occur in EditMode when
            // a LineRenderer's material is accessed for the first time without a scene.
            LogAssert.ignoreFailingMessages = true;

            _go = new GameObject("TestOutlineRenderer");
            _outline = _go.AddComponent<BuildingOutlineRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private T GetFieldValue<T>(object obj, string name)
            => (T)GetField(obj, name)?.GetValue(obj);

        // ── Configure: children are created ──────────────────────────────────

        [Test]
        public void Configure_CreatesLineRendererChild()
        {
            _outline.Configure(Color.cyan, 0.1f, false, Color.clear);

            var lineRenderer = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.IsNotNull(lineRenderer, "Configure should create a LineRenderer child.");
        }

        [Test]
        public void Configure_CreatesFillSpriteRendererChild()
        {
            _outline.Configure(Color.cyan, 0.1f, false, Color.clear);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");

            Assert.IsNotNull(fill, "Configure should create a SpriteRenderer fill child.");
        }

        [Test]
        public void Configure_LineRenderer_HasLoopEnabled()
        {
            _outline.Configure(Color.cyan, 0.1f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.IsTrue(line.loop, "LineRenderer.loop must be true to form a closed rectangle.");
        }

        [Test]
        public void Configure_LineRenderer_HasFourPositions()
        {
            _outline.Configure(Color.cyan, 0.1f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.AreEqual(4, line.positionCount,
                "Rectangle outline requires exactly 4 corner positions.");
        }

        // ── Configure: color / thickness are applied ─────────────────────────

        [Test]
        public void Configure_SetsCyanColor_OnLineRenderer()
        {
            _outline.Configure(Color.cyan, 0.1f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.AreEqual(Color.cyan, line.startColor,
                "startColor must match the configured color (Python hover → cyan 0,255,255).");
            Assert.AreEqual(Color.cyan, line.endColor,
                "endColor must also match the configured color.");
        }

        [Test]
        public void Configure_SetsLineThickness()
        {
            const float expectedThickness = 0.06f;
            _outline.Configure(Color.cyan, expectedThickness, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.AreEqual(expectedThickness, line.startWidth, 0.0001f,
                "startWidth must match the configured thickness.");
            Assert.AreEqual(expectedThickness, line.endWidth, 0.0001f,
                "endWidth must match the configured thickness.");
        }

        [Test]
        public void Configure_DrawFillFalse_FillDisabled()
        {
            _outline.Configure(Color.cyan, 0.06f, drawFill: false, fillColor: Color.clear);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");

            Assert.IsFalse(fill.enabled,
                "Fill SpriteRenderer must be disabled when drawFill=false (no fill for hover outline).");
        }

        [Test]
        public void Configure_DrawFillTrue_FillEnabled()
        {
            var fillColor = new Color(1f, 0f, 0f, 60f / 255f);
            _outline.Configure(Color.red, 0.1f, drawFill: true, fillColor: fillColor);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");

            Assert.IsTrue(fill.enabled,
                "Fill SpriteRenderer must be enabled when drawFill=true " +
                "(Python remove-mode: red fill alpha 60).");
        }

        [Test]
        public void Configure_FillColor_IsApplied()
        {
            var fillColor = new Color(1f, 0f, 0f, 60f / 255f);
            _outline.Configure(Color.red, 0.1f, drawFill: true, fillColor: fillColor);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");

            Assert.AreEqual(fillColor.r, fill.color.r, 0.002f, "Fill color R must match.");
            Assert.AreEqual(fillColor.a, fill.color.a, 0.002f, "Fill color A (alpha 60) must match.");
        }

        // ── SetVisible ──────────────────────────────────────────────────────

        [Test]
        public void SetVisible_False_HidesLineRenderer()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            _outline.SetVisible(false);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");
            Assert.IsFalse(line.enabled, "SetVisible(false) must disable the LineRenderer.");
        }

        [Test]
        public void SetVisible_True_ShowsLineRenderer()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);
            _outline.SetVisible(false);

            _outline.SetVisible(true);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");
            Assert.IsTrue(line.enabled, "SetVisible(true) must re-enable the LineRenderer.");
        }

        [Test]
        public void SetVisible_True_DoesNotShowFill_WhenDrawFillFalse()
        {
            _outline.Configure(Color.cyan, 0.06f, drawFill: false, fillColor: Color.clear);

            _outline.SetVisible(true);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");
            Assert.IsFalse(fill.enabled,
                "Fill must remain hidden even after SetVisible(true) when drawFill=false.");
        }

        [Test]
        public void SetVisible_True_ShowsFill_WhenDrawFillTrue()
        {
            _outline.Configure(Color.red, 0.1f, drawFill: true, fillColor: new Color(1, 0, 0, 0.24f));
            _outline.SetVisible(false);

            _outline.SetVisible(true);

            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");
            Assert.IsTrue(fill.enabled,
                "Fill must be shown by SetVisible(true) when drawFill=true.");
        }

        // ── Follow / target tracking ─────────────────────────────────────────

        [Test]
        public void Follow_NullTarget_DoesNotThrow()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            Assert.DoesNotThrow(() => _outline.Follow(null),
                "Follow(null) should not throw; it is the standard way to detach the outline.");
        }

        [Test]
        public void Follow_ValidTarget_StoresReference()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            var targetGo = new GameObject("TargetBuilding");
            var bObj     = targetGo.AddComponent<BuildingObject>();

            _outline.Follow(bObj);

            var stored = GetFieldValue<BuildingObject>(_outline, "_target");
            Assert.AreEqual(bObj, stored, "Follow should store the BuildingObject reference.");

            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void Follow_Null_AfterSettingTarget_ClearsReference()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            var targetGo = new GameObject("TargetBuilding");
            var bObj     = targetGo.AddComponent<BuildingObject>();
            _outline.Follow(bObj);

            _outline.Follow(null);

            var stored = GetFieldValue<BuildingObject>(_outline, "_target");
            Assert.IsNull(stored, "Follow(null) should clear the stored _target reference.");

            Object.DestroyImmediate(targetGo);
        }

        // ── SortingLayer / sortingOrder ──────────────────────────────────────

        [Test]
        public void Configure_LineRenderer_SortingLayer_IsVFX()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.AreEqual("VFX", line.sortingLayerName,
                "LineRenderer must render on the VFX sorting layer to appear above buildings.");
        }

        [Test]
        public void Configure_FillRenderer_SortingOrder_IsBelowLine()
        {
            _outline.Configure(Color.cyan, 0.06f, true, Color.red);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");
            var fill = GetFieldValue<SpriteRenderer>(_outline, "_fill");

            Assert.Less(fill.sortingOrder, line.sortingOrder,
                "Fill sortingOrder must be below line sortingOrder so the outline renders on top of the fill.");
        }

        // ── UseWorldSpace ────────────────────────────────────────────────────

        [Test]
        public void Configure_LineRenderer_UsesWorldSpace()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.IsTrue(line.useWorldSpace,
                "LineRenderer.useWorldSpace must be true so the outline tracks the building in world coords.");
        }

        // ── Reconfigure idempotency ──────────────────────────────────────────

        [Test]
        public void Configure_CalledTwice_ReusesExistingChildren_DoesNotDuplicate()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);
            int childCountFirst = _go.transform.childCount;

            _outline.Configure(Color.yellow, 0.15f, false, Color.clear);
            int childCountSecond = _go.transform.childCount;

            Assert.AreEqual(childCountFirst, childCountSecond,
                "Configure called twice must reuse existing child GameObjects, not create duplicates.");
        }

        [Test]
        public void Configure_CalledTwice_UpdatesColor()
        {
            _outline.Configure(Color.cyan, 0.06f, false, Color.clear);
            _outline.Configure(Color.yellow, 0.15f, false, Color.clear);

            var line = GetFieldValue<LineRenderer>(_outline, "_line");

            Assert.AreEqual(Color.yellow, line.startColor,
                "Second Configure call should update the line color to the new value.");
        }
    }
}
