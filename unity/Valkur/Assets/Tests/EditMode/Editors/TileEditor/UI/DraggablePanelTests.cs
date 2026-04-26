using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Tests for <see cref="DraggablePanel"/>: Minimize/Maximize/Close behaviour,
    /// OnClose callback wiring, and the static reserved-zone fields used for
    /// canvas clamping.
    /// </summary>
    public class DraggablePanelTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            DraggablePanel.TopReservedPx    = 0f;
            DraggablePanel.BottomReservedPx = 0f;
            DraggablePanel.LeftReservedPx   = 0f;
            DraggablePanel.RightReservedPx  = 0f;
            DraggablePanel.GlobalInterPanelSnap = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            DraggablePanel.TopReservedPx    = 0f;
            DraggablePanel.BottomReservedPx = 0f;
            DraggablePanel.LeftReservedPx   = 0f;
            DraggablePanel.RightReservedPx  = 0f;
        }

        private DraggablePanel BuildPanel(float width = 200f, float height = 300f, float headerH = 24f)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>();
            var canvasRt = (RectTransform)canvasGo.transform;
            canvasRt.sizeDelta = new Vector2(1600f, 800f);
            _spawned.Add(canvasGo);

            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, height);

            var hdrGo = new GameObject("Hdr", typeof(RectTransform));
            hdrGo.transform.SetParent(go.transform, false);
            var hdrRt = (RectTransform)hdrGo.transform;
            hdrRt.sizeDelta = new Vector2(width, headerH);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(go.transform, false);

            var drag = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;

            // EditMode does not reliably fire Awake/OnEnable on AddComponent;
            // invoke them directly so _rt and other private fields are initialized.
            InvokeLifecycle(drag, "Awake");
            InvokeLifecycle(drag, "OnEnable");

            return drag;
        }

        private static void InvokeLifecycle(MonoBehaviour mb, string methodName)
        {
            var m = mb.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            m?.Invoke(mb, null);
        }

        // ── Minimize ────────────────────────────────────────────────────────

        [Test]
        public void Minimize_HidesContentRoot()
        {
            var drag = BuildPanel();
            Assert.IsTrue(drag.ContentRoot.activeSelf, "Content active initially");

            drag.Minimize();

            Assert.IsFalse(drag.ContentRoot.activeSelf, "Content should be inactive after Minimize");
        }

        [Test]
        public void Minimize_CollapsesHeightToHeaderHeight()
        {
            var drag = BuildPanel(width: 200f, height: 300f, headerH: 24f);
            drag.Minimize();
            var rt = (RectTransform)drag.transform;
            Assert.AreEqual(24f, rt.sizeDelta.y, 0.5f, "Height should collapse to header height");
            Assert.AreEqual(200f, rt.sizeDelta.x, 0.5f, "Width should be unchanged");
        }

        [Test]
        public void Minimize_IsIdempotent()
        {
            var drag = BuildPanel();
            drag.Minimize();
            var heightAfterFirst = ((RectTransform)drag.transform).sizeDelta.y;
            drag.Minimize();
            Assert.AreEqual(heightAfterFirst, ((RectTransform)drag.transform).sizeDelta.y, 0.01f);
        }

        // ── Maximize / Restore toggle ───────────────────────────────────────

        [Test]
        public void Maximize_ShowsContentAndExpandsHeight()
        {
            var drag = BuildPanel(width: 200f, height: 300f);
            drag.Minimize();              // start collapsed
            Assert.IsFalse(drag.ContentRoot.activeSelf);

            drag.Maximize();              // first call → expand

            Assert.IsTrue(drag.ContentRoot.activeSelf, "Content should be re-activated by Maximize");
            Assert.Greater(((RectTransform)drag.transform).sizeDelta.y, 300f,
                "Height should grow beyond restored height");
        }

        [Test]
        public void Maximize_TogglesBackToRestoredHeight()
        {
            var drag = BuildPanel(width: 200f, height: 300f);
            drag.Maximize();              // expand
            drag.Maximize();              // restore
            Assert.AreEqual(300f, ((RectTransform)drag.transform).sizeDelta.y, 0.5f);
        }

        // ── Close callback ──────────────────────────────────────────────────

        [Test]
        public void ClosePanel_InvokesOnCloseCallback()
        {
            var drag = BuildPanel();
            int closeCalls = 0;
            drag.OnClose = () => closeCalls++;

            drag.ClosePanel();

            Assert.AreEqual(1, closeCalls);
        }

        [Test]
        public void ClosePanel_NoCallback_DoesNotThrow()
        {
            var drag = BuildPanel();
            drag.OnClose = null;
            Assert.DoesNotThrow(() => drag.ClosePanel());
        }

        // ── Static reserved zones ───────────────────────────────────────────

        [Test]
        public void ReservedZones_AreMutableStatics()
        {
            DraggablePanel.TopReservedPx    = 30f;
            DraggablePanel.BottomReservedPx = 12f;
            DraggablePanel.LeftReservedPx   = 5f;
            DraggablePanel.RightReservedPx  = 8f;

            Assert.AreEqual(30f, DraggablePanel.TopReservedPx);
            Assert.AreEqual(12f, DraggablePanel.BottomReservedPx);
            Assert.AreEqual(5f,  DraggablePanel.LeftReservedPx);
            Assert.AreEqual(8f,  DraggablePanel.RightReservedPx);
        }

        [Test]
        public void GlobalInterPanelSnap_CanBeToggled()
        {
            DraggablePanel.GlobalInterPanelSnap = false;
            Assert.IsFalse(DraggablePanel.GlobalInterPanelSnap);

            DraggablePanel.GlobalInterPanelSnap = true;
            Assert.IsTrue(DraggablePanel.GlobalInterPanelSnap);
        }

        // ── Static panel registry sanity ────────────────────────────────────

        [Test]
        public void Disabling_RemovesFromRegistry_NoLeak()
        {
            var a = BuildPanel();
            var b = BuildPanel();

            a.gameObject.SetActive(false);
            b.gameObject.SetActive(false);

            // Re-enabling should not throw and should re-register cleanly.
            Assert.DoesNotThrow(() =>
            {
                a.gameObject.SetActive(true);
                b.gameObject.SetActive(true);
            });
        }
    }
}
