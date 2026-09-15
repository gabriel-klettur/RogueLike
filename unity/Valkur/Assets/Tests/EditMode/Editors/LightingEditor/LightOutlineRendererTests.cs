using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.LightingEditor
{
    /// <summary>
    /// Unit tests for <see cref="LightOutlineRenderer"/> — the world-space LineRenderer pair
    /// (outer reach ring + inner clickable centre dot) the Lighting Editor's Alt overlay pools,
    /// one per placed light.
    ///
    /// Two of these are about the property that separates this renderer from its Spawner
    /// sibling: it must keep drawing a light whose GameObject has been deactivated, because the
    /// day/night window and the viewport cull both do that to lights that very much still exist.
    /// </summary>
    [TestFixture]
    public class LightOutlineRendererTests
    {
        private GameObject _go;
        private LightOutlineRenderer _outline;

        [SetUp]
        public void SetUp()
        {
            // EditMode + LineRenderer reaches into Material accessors that sometimes log
            // warnings with no scene loaded. Suppress so the test does not fail on the noise.
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("TestLightOutlineRenderer");
            _outline = _go.AddComponent<LightOutlineRenderer>();
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

        private static void InvokeLateUpdate(LightOutlineRenderer outline)
        {
            var m = outline.GetType().GetMethod("LateUpdate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "LateUpdate must exist — it is what redraws both circles.");
            m.Invoke(outline, null);
        }

        // ── Construction ─────────────────────────────────────────────────────

        [Test]
        public void Configure_CreatesRingAndCenterDot()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);

            Assert.IsNotNull(GetFieldValue<LineRenderer>(_outline, "_ring"),
                "Configure must create the outer reach LineRenderer.");
            Assert.IsNotNull(GetFieldValue<LineRenderer>(_outline, "_centerDot"),
                "Configure must create the inner centre-dot LineRenderer.");
        }

        [Test]
        public void Configure_CalledTwice_DoesNotDuplicateChildren()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            int first = _go.transform.childCount;

            _outline.Configure(Color.cyan, 0.10f, 2f);

            Assert.AreEqual(first, _go.transform.childCount,
                "Reconfiguring must reuse existing children, not stack new ones.");
        }

        [Test]
        public void Configure_RingUsesVfxSortingLayer()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");

            Assert.AreEqual("VFX", ring.sortingLayerName,
                "Outline must render on the VFX sorting layer to sit above world tiles.");
        }

        [Test]
        public void Configure_CenterDotSortsAboveRing()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            Assert.Greater(dot.sortingOrder, ring.sortingOrder,
                "Centre dot must render above the ring so the click marker stays readable.");
        }

        // ── Visibility ───────────────────────────────────────────────────────

        [Test]
        public void SetVisible_TogglesBothLineRenderers()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");

            _outline.SetVisible(false);
            Assert.IsFalse(ring.enabled, "Outer ring must be disabled when hidden.");
            Assert.IsFalse(dot.enabled,  "Centre dot must be disabled when hidden.");

            _outline.SetVisible(true);
            Assert.IsTrue(ring.enabled, "Outer ring must re-enable when shown again.");
            Assert.IsTrue(dot.enabled,  "Centre dot must re-enable when shown again.");
        }

        /// <summary>
        /// The whole reason this type exists beside SpawnerOutlineRenderer. A light's GameObject
        /// is deactivated by the day/night window and by the viewport cull, neither of which
        /// means the light stopped existing — an overlay that hid with them would be blank at
        /// noon and off screen, i.e. exactly when the author cannot see the lights themselves.
        /// </summary>
        [Test]
        public void LateUpdate_KeepsDrawing_WhenTargetGameObjectIsInactive()
        {
            _outline.Configure(Color.yellow, 0.06f, 2f);
            var target = new GameObject("DeactivatedLight");
            try
            {
                target.transform.position = new Vector3(3f, 4f, 0f);
                target.SetActive(false);
                _outline.Follow(target.transform);

                InvokeLateUpdate(_outline);

                var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
                Assert.IsTrue(ring.enabled,
                    "A light hidden by the day/night gate or the viewport cull must still be marked.");
                // Vertex 0 sits at angle 0, i.e. centre + (radius, 0).
                Assert.AreEqual(5f, ring.GetPosition(0).x, 0.001f,
                    "The ring must be centred on the inactive light's own position.");
                Assert.AreEqual(4f, ring.GetPosition(0).y, 0.001f,
                    "The ring must be centred on the inactive light's own position.");
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void LateUpdate_HidesWhenNoTarget()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            _outline.SetVisible(true);
            _outline.Follow(null);

            InvokeLateUpdate(_outline);

            Assert.IsFalse(GetFieldValue<LineRenderer>(_outline, "_ring").enabled,
                "A pooled renderer with no light assigned must draw nothing.");
        }

        // ── Ring colour — authored vs derived ────────────────────────────────

        [Test]
        public void SetColor_RepaintsTheRingOnly()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");
            var dot  = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            Color dotBefore = dot.startColor;

            _outline.SetColor(Color.blue);

            Assert.AreEqual(Color.blue, ring.startColor,
                "SetColor must repaint the ring — it is how a derived light is told apart.");
            Assert.AreEqual(dotBefore, dot.startColor,
                "The centre dot is the click affordance and must not take the ring colour.");
        }

        [Test]
        public void SetColor_PreservesThickness()
        {
            _outline.Configure(Color.yellow, 0.09f, 1f);
            var ring = GetFieldValue<LineRenderer>(_outline, "_ring");

            _outline.SetColor(Color.blue);

            Assert.AreEqual(0.09f, ring.startWidth, 0.0001f,
                "Recolouring must not disturb the configured line width.");
        }

        // ── Hover affordance ─────────────────────────────────────────────────

        [Test]
        public void Hover_DefaultsToFalse()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            Assert.IsFalse(_outline.IsHovered, "Hovered must default to false on a fresh renderer.");
        }

        [Test]
        public void SetHovered_True_ChangesCenterDotColorAndThickness()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var dot = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            Color idleColor     = dot.startColor;
            float idleThickness = dot.startWidth;

            _outline.SetHovered(true);

            Assert.AreNotEqual(idleColor, dot.startColor,
                "Centre-dot colour must change on hover so the click affordance is visible.");
            Assert.Greater(dot.startWidth, idleThickness,
                "Centre-dot thickness must grow on hover.");
            Assert.AreEqual(dot.startWidth, dot.endWidth, 0.0001f,
                "Hovered start/end widths must stay symmetric.");
            Assert.IsTrue(_outline.IsHovered);
        }

        [Test]
        public void SetHovered_False_RestoresIdleVisuals()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            var dot = GetFieldValue<LineRenderer>(_outline, "_centerDot");
            Color idleColor     = dot.startColor;
            float idleThickness = dot.startWidth;

            _outline.SetHovered(true);
            _outline.SetHovered(false);

            Assert.AreEqual(idleColor, dot.startColor, "Centre-dot colour must return to baseline.");
            Assert.AreEqual(idleThickness, dot.startWidth, 0.0001f,
                "Centre-dot thickness must return to baseline.");
            Assert.IsFalse(_outline.IsHovered);
        }

        // ── Radius ───────────────────────────────────────────────────────────

        [Test]
        public void SetRadius_StoresValueAboveMinimum()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            _outline.SetRadius(4.25f);

            Assert.AreEqual(4.25f, GetFieldValue<float>(_outline, "_radius"), 0.0001f,
                "SetRadius must persist a light's real outer radius.");
        }

        [Test]
        public void SetRadius_ZeroOrNegative_FallsBackToDefault()
        {
            _outline.Configure(Color.yellow, 0.06f, 1f);
            _outline.SetRadius(0f);

            Assert.Greater(GetFieldValue<float>(_outline, "_radius"), 0f,
                "SetRadius(0) must clamp upward so a ring is still drawn.");
        }
    }
}
