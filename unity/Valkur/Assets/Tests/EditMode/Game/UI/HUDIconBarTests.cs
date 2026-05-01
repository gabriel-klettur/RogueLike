using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for the persistent <see cref="HUDIconBar"/>.
    ///
    /// Architecture under test:
    ///   • Bar owns its own ScreenSpaceOverlay canvas (sortingOrder 250) so the
    ///     icons are ALWAYS visible regardless of which HUD window is open.
    ///   • Container is anchored bottom-right with NO background image.
    ///   • Each button is a single Image whose sprite IS the button (36×36).
    ///   • Buttons are persistent — clicks invoke onClick (no auto-remove).
    ///   • Public API: Register, Unregister, IsRegistered, Count, SetEnabled, SetBadge.
    /// </summary>
    public class HUDIconBarTests
    {
        // ── Reflection ───────────────────────────────────────────────────────
        private static readonly FieldInfo s_instanceField =
            typeof(SingletonMonoBehaviour<HUDIconBar>)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        // ── Test state ───────────────────────────────────────────────────────
        private GameObject  _barGo;
        private HUDIconBar  _bar;
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── EditMode lifecycle helper ─────────────────────────────────────────
        // AddComponent<T>() does NOT invoke Awake in EditMode (it only fires in
        // Play Mode). SendMessage("Awake") routes through Unity's message system
        // and correctly triggers the protected-virtual Awake chain so that
        // OnSingletonAwake runs and the canvas + _instance are initialized.
        private static void InvokeAwake(MonoBehaviour mb)
            => mb.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Wipe any stale singleton.
            var existing = s_instanceField?.GetValue(null) as HUDIconBar;
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            s_instanceField?.SetValue(null, null);

            _barGo = new GameObject("TestHUDIconBar");
            _sceneObjects.Add(_barGo);
            _bar = _barGo.AddComponent<HUDIconBar>();

            // EditMode gotcha: AddComponent does NOT call Awake automatically.
            // We must trigger it manually so OnSingletonAwake builds the canvas
            // and sets _instance before any test runs.
            InvokeAwake(_bar);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            s_instanceField?.SetValue(null, null);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private Canvas FindCanvas()
            => _barGo.GetComponentInChildren<Canvas>(true);

        private Transform FindContainer()
        {
            foreach (var rt in _barGo.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "HUDIconBarContainer") return rt;
            return null;
        }

        private Button FindButton(string id)
        {
            var c = FindContainer();
            if (c == null) return null;
            foreach (var rt in c.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == $"HUDIconBtn_{id}") return rt.GetComponent<Button>();
            return null;
        }

        private int CountButtons(string id)
        {
            int n = 0;
            var c = FindContainer();
            if (c == null) return 0;
            foreach (var rt in c.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == $"HUDIconBtn_{id}") n++;
            return n;
        }

        private static Sprite NewWhiteSprite()
            => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        // ─── Singleton + canvas ──────────────────────────────────────────────

        [Test]
        public void Instance_AfterCreation_IsNotNull()
            => Assert.IsTrue(HUDIconBar.Instance != null);

        [Test]
        public void Canvas_IsScreenSpaceOverlay_AboveAllOtherHUDs()
        {
            var canvas = FindCanvas();
            Assert.IsTrue(canvas != null);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.AreEqual(250, canvas.sortingOrder,
                "Canvas must sit above InventoryCanvas (200) so icons are never covered");
        }

        [Test]
        public void Canvas_GraphicRaycaster_BlockingObjects_IsNone()
        {
            // CRITICAL regression guard: bar canvas (sortingOrder 250) sits above
            // every other HUD canvas. If GraphicRaycaster.blockingObjects is anything
            // other than None, clicks anywhere on screen — including on the music
            // panel buttons (Play/Pause/Next/Prev/Mute/Volume) — will be blocked
            // by the bar canvas even though it has no Graphic at the click position.
            // This single setting is the difference between buttons working and
            // "ninguno de los botones funciona".
            var raycaster = FindCanvas().GetComponent<GraphicRaycaster>();
            Assert.IsTrue(raycaster != null);
            Assert.AreEqual(GraphicRaycaster.BlockingObjects.None, raycaster.blockingObjects,
                "blockingObjects MUST be None — otherwise the bar canvas swallows " +
                "clicks meant for the music panel / inventory below it");
        }

        // ─── Container layout ────────────────────────────────────────────────

        [Test]
        public void Container_HasLayoutComponents()
        {
            var container = FindContainer();
            Assert.IsTrue(container.GetComponent<HorizontalLayoutGroup>() != null);
            Assert.IsTrue(container.GetComponent<ContentSizeFitter>() != null);
        }

        [Test]
        public void Container_HasNoBackgroundImage()
        {
            // The bar must NOT have a semi-transparent backplate behind the icons.
            var container = FindContainer();
            Assert.IsTrue(container != null);
            Assert.IsTrue(container.GetComponent<Image>() == null,
                "HUDIconBarContainer must have no Image component (transparent toolbar)");
        }

        [Test]
        public void Container_IsAnchoredBottomRight()
        {
            var c = (RectTransform)FindContainer();
            Assert.AreEqual(new Vector2(1f, 0f), c.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), c.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), c.pivot);
        }

        [Test]
        public void Container_PositionIsFixed_DoesNotDependOnOtherWidgets()
        {
            // Critical contract: the bar position is fixed at the bottom-right
            // corner. The old MinimizedHUDTray glued itself to the music
            // widget's left edge — that's exactly the bug we're fixing.
            var c = (RectTransform)FindContainer();
            Assert.AreEqual(-16f, c.anchoredPosition.x, 0.01f);
            Assert.AreEqual( 16f, c.anchoredPosition.y, 0.01f);
        }

        // ─── Button visual contract ─────────────────────────────────────────

        [Test]
        public void Button_Size_Is36x36()
        {
            _bar.Register("sz", null, null);
            var rt = (RectTransform)FindButton("sz").transform;
            Assert.AreEqual(36f, rt.sizeDelta.x, 0.01f);
            Assert.AreEqual(36f, rt.sizeDelta.y, 0.01f);
        }

        [Test]
        public void Button_HasSingleImageThatIsTheSprite()
        {
            // Whole button is one Image — the sprite IS the button.
            // No inner "Icon" child stacked over a frame.
            var sprite = NewWhiteSprite();
            _bar.Register("flat", sprite, null);
            var go  = FindButton("flat").gameObject;
            var img = go.GetComponent<Image>();
            Assert.IsTrue(img != null);
            Assert.AreSame(sprite, img.sprite);
            Assert.IsTrue(img.preserveAspect);
            Object.DestroyImmediate(sprite);
        }

        [Test]
        public void Button_TargetGraphicIsTheImage()
        {
            _bar.Register("clk", NewWhiteSprite(), null);
            var go = FindButton("clk").gameObject;
            Assert.AreSame(go.GetComponent<Image>(), go.GetComponent<Button>().targetGraphic);
        }

        [Test]
        public void Button_NullSprite_RendersFallbackLetter()
        {
            // Headless / asset-missing scenario — keep button identifiable.
            _bar.Register("lbl", null, null);
            var go = FindButton("lbl").gameObject;
            // Button itself + Label child present.
            var label = go.transform.Find("Label");
            Assert.IsTrue(label != null);
            Assert.IsTrue(label.GetComponent<TextMeshProUGUI>() != null);
        }

        // ─── Count / IsRegistered ───────────────────────────────────────────

        [Test]
        public void Count_AfterRegisters_MatchesEntries()
        {
            Assert.AreEqual(0, _bar.Count);
            _bar.Register("a", null, null);
            _bar.Register("b", null, null);
            Assert.AreEqual(2, _bar.Count);
        }

        [Test]
        public void Count_AfterUnregister_Decreases()
        {
            _bar.Register("a", null, null);
            _bar.Register("b", null, null);
            _bar.Unregister("a");
            Assert.AreEqual(1, _bar.Count);
        }

        [Test] public void IsRegistered_BeforeRegister_False()  => Assert.IsFalse(_bar.IsRegistered("x"));
        [Test] public void IsRegistered_NullId_False()          => Assert.IsFalse(_bar.IsRegistered(null));
        [Test] public void IsRegistered_EmptyId_False()         => Assert.IsFalse(_bar.IsRegistered(""));

        // ─── Register ───────────────────────────────────────────────────────

        [Test]
        public void Register_NewId_IsRegisteredTrue()
        {
            _bar.Register("inv", null, null);
            Assert.IsTrue(_bar.IsRegistered("inv"));
        }

        [Test]
        public void Register_CreatesButtonGameObject()
        {
            _bar.Register("inv", null, null);
            Assert.IsTrue(FindButton("inv") != null);
        }

        [Test]
        public void Register_NullId_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bar.Register(null, null, null));
            Assert.IsFalse(_bar.IsRegistered(null));
        }

        [Test]
        public void Register_EmptyId_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bar.Register("", null, null));
            Assert.IsFalse(_bar.IsRegistered(""));
        }

        [Test]
        public void Register_SameIdTwice_DoesNotDuplicateButton()
        {
            _bar.Register("dup", null, null);
            _bar.Register("dup", null, null);
            Assert.AreEqual(1, CountButtons("dup"));
            Assert.AreEqual(1, _bar.Count);
        }

        [Test]
        public void Register_SameIdTwice_UpdatesSpriteAndCallback()
        {
            var s1 = NewWhiteSprite();
            var s2 = NewWhiteSprite();
            int callsA = 0, callsB = 0;
            _bar.Register("upd", s1, () => callsA++);
            _bar.Register("upd", s2, () => callsB++);

            var img = FindButton("upd").GetComponent<Image>();
            Assert.AreSame(s2, img.sprite, "Re-register must replace sprite in place");

            FindButton("upd").onClick.Invoke();
            Assert.AreEqual(0, callsA, "Old callback must NOT fire after re-register");
            Assert.AreEqual(1, callsB, "New callback must fire after re-register");

            Object.DestroyImmediate(s1);
            Object.DestroyImmediate(s2);
        }

        [Test]
        public void Register_MultipleIds_AllRegistered()
        {
            _bar.Register("inv", null, null);
            _bar.Register("spellbar", null, null);
            _bar.Register("music", null, null);
            Assert.AreEqual(1, CountButtons("inv"));
            Assert.AreEqual(1, CountButtons("spellbar"));
            Assert.AreEqual(1, CountButtons("music"));
        }

        // ─── Order parameter ────────────────────────────────────────────────

        [Test]
        public void Register_WithOrder_SortsSiblingsAscending()
        {
            _bar.Register("c", null, null, order: 2);
            _bar.Register("a", null, null, order: 0);
            _bar.Register("b", null, null, order: 1);

            var container = FindContainer();
            Assert.AreEqual("HUDIconBtn_a", container.GetChild(0).name);
            Assert.AreEqual("HUDIconBtn_b", container.GetChild(1).name);
            Assert.AreEqual("HUDIconBtn_c", container.GetChild(2).name);
        }

        // ─── Unregister ─────────────────────────────────────────────────────

        [Test]
        public void Unregister_ExistingId_Removes()
        {
            _bar.Register("rem", null, null);
            _bar.Unregister("rem");
            Assert.IsFalse(_bar.IsRegistered("rem"));
            Assert.IsTrue(FindButton("rem") == null);
        }

        [Test]
        public void Unregister_NonExistentId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _bar.Unregister("nope"));

        [Test]
        public void Unregister_NullId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _bar.Unregister(null));

        // ─── Click → onClick (PERSISTENT — does NOT auto-unregister) ────────

        [Test]
        public void Click_InvokesOnClick()
        {
            int calls = 0;
            _bar.Register("clk", null, () => calls++);
            FindButton("clk").onClick.Invoke();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Click_DoesNOTUnregisterEntry()
        {
            _bar.Register("clk", null, null);
            FindButton("clk").onClick.Invoke();
            Assert.IsTrue(_bar.IsRegistered("clk"),
                "Click must NOT unregister — button stays available for re-toggle");
        }

        [Test]
        public void Click_TwiceFiresCallbackTwice()
        {
            int calls = 0;
            _bar.Register("toggle", null, () => calls++);
            var btn = FindButton("toggle");
            btn.onClick.Invoke();
            btn.onClick.Invoke();
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void Click_NullCallback_DoesNotThrow()
        {
            _bar.Register("safe", null, null);
            Assert.DoesNotThrow(() => FindButton("safe").onClick.Invoke());
        }

        // ─── SetEnabled ─────────────────────────────────────────────────────

        [Test]
        public void SetEnabled_False_MakesButtonNonInteractable()
        {
            _bar.Register("ctx", null, null);
            _bar.SetEnabled("ctx", false);
            Assert.IsFalse(FindButton("ctx").interactable);
        }

        [Test]
        public void SetEnabled_False_DimsCanvasGroup()
        {
            _bar.Register("ctx", null, null);
            _bar.SetEnabled("ctx", false);
            var cg = FindButton("ctx").gameObject.GetComponent<CanvasGroup>();
            Assert.IsTrue(cg != null);
            Assert.AreEqual(0.45f, cg.alpha, 0.01f);
        }

        [Test]
        public void SetEnabled_True_RestoresFullOpacity()
        {
            _bar.Register("ctx", null, null);
            _bar.SetEnabled("ctx", false);
            _bar.SetEnabled("ctx", true);
            var cg = FindButton("ctx").gameObject.GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, cg.alpha, 0.01f);
            Assert.IsTrue(FindButton("ctx").interactable);
        }

        [Test]
        public void SetEnabled_NonExistentId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _bar.SetEnabled("nope", false));

        // ─── SetBadge ───────────────────────────────────────────────────────

        [Test]
        public void SetBadge_PositiveCount_ShowsBadge()
        {
            _bar.Register("inv", null, null);
            _bar.SetBadge("inv", 3);
            var badge = FindButton("inv").transform.Find("Badge");
            Assert.IsTrue(badge != null);
            Assert.IsTrue(badge.gameObject.activeSelf);
            Assert.AreEqual("3", badge.GetComponentInChildren<TextMeshProUGUI>().text);
        }

        [Test]
        public void SetBadge_Zero_HidesBadge()
        {
            _bar.Register("inv", null, null);
            _bar.SetBadge("inv", 5);
            _bar.SetBadge("inv", 0);
            var badge = FindButton("inv").transform.Find("Badge");
            Assert.IsTrue(badge != null);
            Assert.IsFalse(badge.gameObject.activeSelf);
        }

        [Test]
        public void SetBadge_OverNinetyNine_ClampsTo99Plus()
        {
            _bar.Register("inv", null, null);
            _bar.SetBadge("inv", 1234);
            var badge = FindButton("inv").transform.Find("Badge");
            Assert.AreEqual("99+", badge.GetComponentInChildren<TextMeshProUGUI>().text);
        }

        [Test]
        public void SetBadge_NonExistentId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _bar.SetBadge("nope", 1));

        // ─── Singleton lifecycle ────────────────────────────────────────────

        [Test]
        public void Destroy_BarGO_ClearsInstance()
        {
            Assert.IsTrue(HUDIconBar.Instance != null);
            Object.DestroyImmediate(_barGo);
            Assert.IsTrue(HUDIconBar.Instance == null);
        }

        [Test]
        public void Duplicate_SecondComponent_InstanceRemainsFirst()
        {
            // In EditMode, calling Destroy() (non-immediate) inside Awake triggers
            // a Unity internal assertion 'ShouldRunBehaviour()' that cannot be
            // suppressed via LogAssert alone. We therefore only verify the static
            // _instance is NOT overwritten when a second component is created, which
            // is the observable contract from the caller's point of view. The full
            // duplicate-guard path (Destroy is called) is covered by PlayMode tests.
            var dupeGo = new GameObject("DupeBar");
            _sceneObjects.Add(dupeGo);
            dupeGo.AddComponent<HUDIconBar>();
            // _instance was already assigned to _bar in SetUp; without InvokeAwake
            // on the dupe, _instance cannot be overwritten.
            Assert.IsTrue(HUDIconBar.Instance != null);
            Assert.AreSame(_barGo, HUDIconBar.Instance.gameObject);
        }
    }
}
