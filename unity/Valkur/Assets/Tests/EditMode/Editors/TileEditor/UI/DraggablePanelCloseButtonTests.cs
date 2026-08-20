using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// The header close button and its remembered state.
    ///
    /// Every assertion here maps to something that was actually broken when the button
    /// first shipped: it was never built, because the builders assign DragHeader on the
    /// line AFTER AddComponent and OnEnable fires synchronously inside AddComponent; and
    /// clicking it would not have closed anything, because ClosePanel only raised a
    /// callback that no editor uses for hiding.
    ///
    /// The chrome is driven through EnsureChrome rather than by enabling the object,
    /// because Unity does not run OnEnable in Edit Mode without [ExecuteAlways].
    /// </summary>
    [TestFixture]
    public class DraggablePanelCloseButtonTests
    {
        private const string KEY = "__test_panel_key";
        private readonly List<GameObject> _objects = new List<GameObject>();

        private DraggablePanel BuildPanel(bool withHeader = true, string key = KEY)
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas), typeof(RectTransform));
            _objects.Add(canvasGo);

            var panelGo = new GameObject("TestPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);

            var drag = panelGo.AddComponent<DraggablePanel>();
            if (withHeader)
            {
                var hdr = new GameObject("PanelHeader", typeof(RectTransform));
                hdr.transform.SetParent(panelGo.transform, false);
                drag.DragHeader = (RectTransform)hdr.transform;
            }
            drag.PersistenceKey = key;
            return drag;
        }

        private static Transform FindButton(DraggablePanel drag)
            => drag.DragHeader == null ? null : drag.DragHeader.Find("PanelCloseButton");

        [SetUp]
        public void SetUp() => DraggablePanel.ForgetAllPanelStates(KEY, "OtherKey");

        [TearDown]
        public void TearDown()
        {
            DraggablePanel.ForgetAllPanelStates(KEY, "OtherKey");
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // ── Construction ────────────────────────────────────────────────────

        [Test]
        public void EnsureChrome_BuildsCloseButton_InTheHeader()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();

            var btn = FindButton(drag);
            Assert.IsNotNull(btn, "EnsureChrome must add a close button to the header.");
            Assert.IsNotNull(btn.GetComponent<Button>(), "The close button needs a Button.");
        }

        [Test]
        public void CloseButton_IsAnchoredToTheTopRightCorner()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();

            var rt = (RectTransform)FindButton(drag);
            Assert.AreEqual(1f, rt.anchorMin.x, 0.001f, "Must anchor to the right edge.");
            Assert.AreEqual(1f, rt.anchorMax.x, 0.001f, "Must anchor to the right edge.");
            Assert.AreEqual(1f, rt.pivot.x, 0.001f, "Pivot must sit on its own right edge.");
            Assert.Less(rt.anchoredPosition.x, 0f, "Must be inset from the corner, not past it.");
        }

        /// <summary>
        /// An Image and a TMP text on the same GameObject throw a NullReferenceException in
        /// this project, so the label has to live on a child.
        /// </summary>
        [Test]
        public void CloseButton_LabelIsOnAChild_NotOnTheButtonObject()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();
            var btn = FindButton(drag);

            Assert.IsNotNull(btn.GetComponent<Image>(), "The button itself carries the Image.");
            Assert.IsNull(btn.GetComponent<TextMeshProUGUI>(),
                "A TMP text on the same object as the Image is the documented NRE.");
            Assert.IsNotNull(btn.GetComponentInChildren<TextMeshProUGUI>(),
                "The label must exist, on a child.");
        }

        [Test]
        public void EnsureChrome_IsIdempotent()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();
            drag.EnsureChrome();
            drag.EnsureChrome();

            int found = 0;
            foreach (Transform c in drag.DragHeader)
                if (c.name == "PanelCloseButton") found++;
            Assert.AreEqual(1, found, "Repeated calls must not stack up buttons.");
        }

        [Test]
        public void EnsureChrome_WithoutHeader_DoesNotThrow()
        {
            var drag = BuildPanel(withHeader: false);
            Assert.DoesNotThrow(() => drag.EnsureChrome(),
                "A panel with no header must be tolerated, not crash the editor build.");
        }

        [Test]
        public void EnsureChrome_WhenOptedOut_BuildsNothing()
        {
            var drag = BuildPanel();
            drag.ShowCloseButton = false;
            drag.EnsureChrome();

            Assert.IsNull(FindButton(drag), "ShowCloseButton=false must suppress the button.");
        }

        // ── Closing actually closes ─────────────────────────────────────────

        /// <summary>
        /// The bug that made the button useless: ClosePanel only raised OnClose, and every
        /// editor handler just updates its menu highlight. Nothing hid the panel.
        /// </summary>
        [Test]
        public void ClosePanel_HidesThePanel()
        {
            var drag = BuildPanel();
            Assert.IsTrue(drag.gameObject.activeSelf, "Panel starts visible.");

            drag.ClosePanel();

            Assert.IsFalse(drag.gameObject.activeSelf,
                "ClosePanel must hide the panel, not merely announce it.");
        }

        [Test]
        public void ClosePanel_StillInvokesOnClose_SoHostsStayInSync()
        {
            var drag = BuildPanel();
            int calls = 0;
            drag.OnClose = () => calls++;

            drag.ClosePanel();

            Assert.AreEqual(1, calls, "Hosts rely on this to update their menu highlight.");
        }

        [Test]
        public void ClickingTheButton_HidesThePanel_AndNotifiesTheHost()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();
            int calls = 0;
            drag.OnClose = () => calls++;

            FindButton(drag).GetComponent<Button>().onClick.Invoke();

            Assert.IsFalse(drag.gameObject.activeSelf, "The X must hide the panel.");
            Assert.AreEqual(1, calls, "The X must notify the host exactly once.");
        }

        // ── Persistence ─────────────────────────────────────────────────────

        [Test]
        public void ClickingTheButton_RemembersThatItWasClosed()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();
            Assert.IsFalse(drag.WasClosedLastSession, "Starts unremembered.");

            FindButton(drag).GetComponent<Button>().onClick.Invoke();

            Assert.IsTrue(drag.WasClosedLastSession, "Closing must persist.");
        }

        [Test]
        public void MarkOpened_ClearsTheRememberedClose()
        {
            var drag = BuildPanel();
            drag.EnsureChrome();
            FindButton(drag).GetComponent<Button>().onClick.Invoke();

            drag.MarkOpened();

            Assert.IsFalse(drag.WasClosedLastSession, "Re-opening must be remembered too.");
        }

        [Test]
        public void ApplyRememberedVisibility_ClosedLastSession_HidesAndAnnounces()
        {
            var first = BuildPanel();
            first.EnsureChrome();
            FindButton(first).GetComponent<Button>().onClick.Invoke();   // remembers closed

            var next = BuildPanel();                                     // same PersistenceKey
            int restored = 0, closed = 0;
            next.OnRestoredClosed = () => restored++;
            next.OnClose = () => closed++;

            next.ApplyRememberedVisibility();

            Assert.IsFalse(next.gameObject.activeSelf, "A panel closed last session stays closed.");
            Assert.AreEqual(1, restored,
                "OnRestoredClosed must fire, or the menu bar shows the panel as open.");
            Assert.AreEqual(1, closed, "The host normal close path must run.");
        }

        [Test]
        public void ApplyRememberedVisibility_NotClosedLastSession_LeavesPanelAlone()
        {
            var drag = BuildPanel();
            int restored = 0;
            drag.OnRestoredClosed = () => restored++;

            drag.ApplyRememberedVisibility();

            Assert.IsTrue(drag.gameObject.activeSelf, "An unremembered panel must stay visible.");
            Assert.AreEqual(0, restored, "Nothing to restore, so nothing to announce.");
        }

        [Test]
        public void PersistenceKey_SeparatesPanels()
        {
            var a = BuildPanel(key: KEY);
            a.EnsureChrome();
            FindButton(a).GetComponent<Button>().onClick.Invoke();

            var b = BuildPanel(key: "OtherKey");
            Assert.IsFalse(b.WasClosedLastSession,
                "Closing one panel must not close an unrelated one.");
        }

        [Test]
        public void PersistenceKey_DefaultsToTheGameObjectName()
        {
            var drag = BuildPanel(key: null);
            drag.PersistenceKey = null;
            Assert.DoesNotThrow(() => { var _ = drag.WasClosedLastSession; },
                "A panel with no explicit key must fall back to its name, not throw.");
        }
    }
}
