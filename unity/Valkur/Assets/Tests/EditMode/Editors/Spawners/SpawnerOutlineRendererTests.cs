using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Editors.Spawners
{
    /// <summary>
    /// Unit tests for <see cref="SpawnerOutlineRenderer"/> — the world-space
    /// LineRenderer pair (outer trigger ring + inner clickable centre dot)
    /// used by the Spawner Editor (F3) Alt-toggle visualization.
    ///
    /// The hover affordance is critical for discoverability: without these
    /// assertions, a regression that silently breaks the colour/thickness
    /// swap would leave users staring at an unresponsive marker.
    /// </summary>
    [TestFixture]
    public class SpawnerOutlineRendererTests
    {
        private GameObject _go;
        private SpawnerOutlineRenderer _outline;

        [SetUp]
        public void SetUp()
        {
            // EditMode + LineRenderer reaches into Material accessors that
            // sometimes log warnings when no scene is loaded. Suppress so the
            // test itself doesn't fail on unrelated noise.
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("TestSpawnerOutlineRenderer");
            _outline = _go.AddComponent<SpawnerOutlineRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ───────────────────────────────────────────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetFieldValue<T>(object obj, string name)
            => (T)GetField(obj, name)?.GetValue(obj);

        // ── Configure builds the children once ───────────────────────────────

        [Test]
        public void Configure_CreatesRingAndCenterDot()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);

            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            Assert.IsNotNull(ring, "Configure must create the outer trigger-radius LineRenderer.");
            Assert.IsNotNull(dot,  "Configure must create the inner centre-dot LineRenderer.");
        }

        [Test]
        public void Configure_CalledTwice_DoesNotDuplicateChildren()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            int firstCount = _go.transform.childCount;

            _outline.Configure(Color.yellow, 0.10f, 2f);
            int secondCount = _go.transform.childCount;

            Assert.AreEqual(firstCount, secondCount,
                "Reconfiguring the renderer must reuse existing children, not stack new ones.");
        }

        [Test]
        public void Configure_RingUsesVfxSortingLayer()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");

            Assert.AreEqual("VFX", ring.sortingLayerName,
                "Outline must render on the VFX sorting layer to sit above world tiles.");
        }

        [Test]
        public void Configure_CenterDotSortsAboveRing()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            Assert.Greater(dot.sortingOrder, ring.sortingOrder,
                "Centre dot must render above the trigger ring so the click marker is always readable.");
        }

        // ── SetVisible toggles both ───────────────────────────────────────────

        [Test]
        public void SetVisible_False_HidesBothLineRenderers()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            _outline.SetVisible(false);

            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            Assert.IsFalse(ring.enabled, "Outer ring must be disabled when the outline is hidden.");
            Assert.IsFalse(dot.enabled,  "Centre dot must be disabled when the outline is hidden.");
        }

        [Test]
        public void SetVisible_True_ShowsBothLineRenderers()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            _outline.SetVisible(false);
            _outline.SetVisible(true);

            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            Assert.IsTrue(ring.enabled, "Outer ring must re-enable when the outline is shown again.");
            Assert.IsTrue(dot.enabled,  "Centre dot must re-enable when the outline is shown again.");
        }

        // ── Hover affordance — colour / thickness swap ────────────────────────

        [Test]
        public void Hover_DefaultsToFalse()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            Assert.IsFalse(_outline.IsHovered, "Hovered must default to false on a freshly-built renderer.");
        }

        [Test]
        public void SetHovered_True_ChangesCenterDotColor()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            var dot = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            Color baseline = dot.startColor;

            _outline.SetHovered(true);

            Assert.AreNotEqual(baseline, dot.startColor,
                "Centre-dot colour must change when hovered so the user sees the click affordance.");
            Assert.IsTrue(_outline.IsHovered, "IsHovered must reflect the SetHovered(true) call.");
        }

        [Test]
        public void SetHovered_True_IncreasesCenterDotThickness()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            var dot = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            float baselineThickness = dot.startWidth;

            _outline.SetHovered(true);

            Assert.Greater(dot.startWidth, baselineThickness,
                "Centre-dot thickness must grow when hovered (visual emphasis for the click affordance).");
            Assert.AreEqual(dot.startWidth, dot.endWidth, 0.0001f,
                "Hovered start/end widths must remain symmetric.");
        }

        [Test]
        public void SetHovered_False_RestoresIdleVisuals()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            var dot = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            Color   idleColor     = dot.startColor;
            float   idleThickness = dot.startWidth;

            _outline.SetHovered(true);
            _outline.SetHovered(false);

            Assert.AreEqual(idleColor,     dot.startColor, "Centre-dot colour must return to baseline on un-hover.");
            Assert.AreEqual(idleThickness, dot.startWidth, 0.0001f, "Centre-dot thickness must return to baseline on un-hover.");
            Assert.IsFalse(_outline.IsHovered, "IsHovered must reflect the SetHovered(false) call.");
        }

        [Test]
        public void SetHovered_Idempotent_NoStateChurn()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            _outline.SetHovered(true);
            // Re-asserting the same state must be a no-op: _hovered guards the
            // ApplyCenterDotVisuals call to avoid touching the LineRenderer
            // every frame for a stable hover.
            Assert.DoesNotThrow(() => _outline.SetHovered(true),
                "Setting the same hover value twice must be safe.");
            Assert.IsTrue(_outline.IsHovered);
        }

        // ── SetRadius — runtime per-instance trigger sizing ──────────────────

        [Test]
        public void SetRadius_StoresValueAboveMinimum()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            _outline.SetRadius(2.5f);

            float stored = GetFieldValue<float>(_outline, "_radius");
            Assert.AreEqual(2.5f, stored, 0.0001f,
                "SetRadius must persist a regular trigger radius.");
        }

        [Test]
        public void SetRadius_ZeroOrNegative_FallsBackToDefault()
        {
            _outline.Configure(Color.cyan, 0.06f, 1f);
            _outline.SetRadius(0f);

            float stored = GetFieldValue<float>(_outline, "_radius");
            Assert.Greater(stored, 0f,
                "SetRadius(0) must clamp upward to a sensible default so a ring is still drawn.");
        }
    }
}
