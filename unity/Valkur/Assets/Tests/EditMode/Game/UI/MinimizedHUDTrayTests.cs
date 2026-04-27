using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for the persistent MinimizedHUDTray.
    ///
    /// Architecture under test:
    ///   • Container is parented under the SAME canvas as MusicPlayerHUD.
    ///   • Each button is a single Image whose sprite IS the button.
    ///   • Buttons are persistent; clicking invokes onToggle (no auto-remove).
    /// </summary>
    public class MinimizedHUDTrayTests
    {
        // ── Reflection ───────────────────────────────────────────────────────
        private static readonly FieldInfo s_instanceField =
            typeof(SingletonMonoBehaviour<MinimizedHUDTray>)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo s_musicRtField =
            typeof(MinimizedHUDTray)
                .GetField("_musicRt", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_ensureContainerBuilt =
            typeof(MinimizedHUDTray)
                .GetMethod("EnsureContainerBuilt", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_pinContainer =
            typeof(MinimizedHUDTray)
                .GetMethod("PinContainerToMusicLeft", BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Test state ───────────────────────────────────────────────────────
        private GameObject       _trayGo;
        private MinimizedHUDTray _tray;
        private GameObject       _musicCanvasGo;
        private RectTransform    _mockMusicWidgetRt;
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Wipe any stale singleton.
            var existing = s_instanceField?.GetValue(null) as MinimizedHUDTray;
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            s_instanceField?.SetValue(null, null);

            // Mock music canvas + widget so the tray has a parent to attach to.
            _musicCanvasGo = new GameObject("MusicHUDCanvas");
            _sceneObjects.Add(_musicCanvasGo);
            var canvas = _musicCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _musicCanvasGo.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _musicCanvasGo.AddComponent<GraphicRaycaster>();

            var widgetGo = new GameObject("MusicWidgetMock", typeof(RectTransform));
            widgetGo.transform.SetParent(_musicCanvasGo.transform, false);
            _mockMusicWidgetRt = (RectTransform)widgetGo.transform;
            _mockMusicWidgetRt.anchorMin = new Vector2(1f, 0f);
            _mockMusicWidgetRt.anchorMax = new Vector2(1f, 0f);
            _mockMusicWidgetRt.pivot     = new Vector2(1f, 0f);
            _mockMusicWidgetRt.sizeDelta = new Vector2(36f, 36f);
            _mockMusicWidgetRt.anchoredPosition = new Vector2(-16f, 16f);

            // Create tray.
            _trayGo = new GameObject("TestMinimizedHUDTray");
            _sceneObjects.Add(_trayGo);
            _tray = _trayGo.AddComponent<MinimizedHUDTray>();
            if (MinimizedHUDTray.Instance == null)
                s_instanceField?.SetValue(null, _tray);

            // Force-resolve + build (LateUpdate is off in EditMode).
            s_musicRtField?.SetValue(_tray, _mockMusicWidgetRt);
            s_ensureContainerBuilt?.Invoke(_tray, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            s_instanceField?.SetValue(null, null);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private Transform FindContainer()
        {
            foreach (var rt in _musicCanvasGo.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "MinimizedHUDTrayContainer") return rt;
            return null;
        }

        private Button FindButton(string id)
        {
            var c = FindContainer();
            if (c == null) return null;
            foreach (var rt in c.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == $"TrayBtn_{id}") return rt.GetComponent<Button>();
            return null;
        }

        private int CountButtons(string id)
        {
            int n = 0;
            var c = FindContainer();
            if (c == null) return 0;
            foreach (var rt in c.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == $"TrayBtn_{id}") n++;
            return n;
        }

        private static Sprite NewWhiteSprite()
            => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        // ─── Initialization ──────────────────────────────────────────────────────

        [Test]
        public void Instance_AfterCreation_IsNotNull()
            => Assert.IsTrue(MinimizedHUDTray.Instance != null);

        [Test]
        public void Container_IsParentedUnderMusicCanvas()
        {
            var container = FindContainer();
            Assert.IsTrue(container != null);
            Assert.AreSame(_musicCanvasGo.transform, container.parent);
        }

        [Test]
        public void Container_HasLayoutComponents()
        {
            var container = FindContainer();
            Assert.IsTrue(container.GetComponent<HorizontalLayoutGroup>() != null);
            Assert.IsTrue(container.GetComponent<ContentSizeFitter>() != null);
        }

        [Test]
        public void Container_BeforeAnyRegister_IsInactive()
            => Assert.IsFalse(FindContainer().gameObject.activeSelf);

        [Test]
        public void Container_AfterRegister_BecomesActive()
        {
            _tray.Register("vis", null, null);
            Assert.IsTrue(FindContainer().gameObject.activeSelf);
        }

        [Test]
        public void Container_AfterUnregisterLast_BecomesInactive()
        {
            _tray.Register("vis", null, null);
            _tray.Unregister("vis");
            Assert.IsFalse(FindContainer().gameObject.activeSelf);
        }

        [Test]
        public void Container_MirrorsMusicAnchors()
        {
            var c = (RectTransform)FindContainer();
            Assert.AreEqual(_mockMusicWidgetRt.anchorMin, c.anchorMin);
            Assert.AreEqual(_mockMusicWidgetRt.anchorMax, c.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), c.pivot);
        }

        // ─── Button visual contract ─────────────────────────────────────────────

        [Test]
        public void Button_Size_Is36x36()
        {
            _tray.Register("sz", null, null);
            var rt = (RectTransform)FindButton("sz").transform;
            Assert.AreEqual(36f, rt.sizeDelta.x, 0.01f);
            Assert.AreEqual(36f, rt.sizeDelta.y, 0.01f);
        }

        [Test]
        public void Button_HasSingleImageThatIsTheSprite()
        {
            // The whole button is one Image — the sprite is the button.
            // No inner "Icon" child stacked over a frame.
            var sprite = NewWhiteSprite();
            _tray.Register("flat", sprite, null);
            var go  = FindButton("flat").gameObject;
            var img = go.GetComponent<Image>();
            Assert.IsTrue(img != null);
            Assert.AreSame(sprite, img.sprite);
            Assert.IsTrue(img.preserveAspect);
            Assert.AreEqual(0, go.transform.childCount,
                "When a sprite is provided, button must have NO child (no inner icon over frame)");
            Object.DestroyImmediate(sprite);
        }

        [Test]
        public void Button_TargetGraphicIsTheImage()
        {
            _tray.Register("clk", NewWhiteSprite(), null);
            var go = FindButton("clk").gameObject;
            Assert.AreSame(go.GetComponent<Image>(), go.GetComponent<Button>().targetGraphic);
        }

        [Test]
        public void Button_NullSprite_RendersFallbackLetter()
        {
            // Headless / asset-missing scenario — keep button identifiable.
            _tray.Register("lbl", null, null);
            var go = FindButton("lbl").gameObject;
            Assert.AreEqual(1, go.transform.childCount);
            Assert.AreEqual("Label", go.transform.GetChild(0).name);
        }

        // ─── Positioning ────────────────────────────────────────────────────────

        [Test]
        public void PinContainer_PlacesItToLeftOfMusicWithGap()
        {
            _tray.Register("p", null, null);
            s_pinContainer?.Invoke(_tray, null);

            var container = (RectTransform)FindContainer();
            float expectedX = _mockMusicWidgetRt.anchoredPosition.x
                            - _mockMusicWidgetRt.rect.width
                            - 8f /* TRAY_GAP */;
            Assert.AreEqual(expectedX, container.anchoredPosition.x, 0.01f);
            Assert.AreEqual(_mockMusicWidgetRt.anchoredPosition.y, container.anchoredPosition.y, 0.01f);
        }

        // ─── Count ──────────────────────────────────────────────────────────────

        [Test]
        public void Count_AfterRegisters_MatchesEntries()
        {
            Assert.AreEqual(0, _tray.Count);
            _tray.Register("a", null, null);
            _tray.Register("b", null, null);
            Assert.AreEqual(2, _tray.Count);
        }

        [Test]
        public void Count_AfterUnregister_Decreases()
        {
            _tray.Register("a", null, null);
            _tray.Register("b", null, null);
            _tray.Unregister("a");
            Assert.AreEqual(1, _tray.Count);
        }

        // ─── IsRegistered ──────────────────────────────────────────────────────

        [Test] public void IsRegistered_BeforeRegister_False()  => Assert.IsFalse(_tray.IsRegistered("x"));
        [Test] public void IsRegistered_NullId_False()          => Assert.IsFalse(_tray.IsRegistered(null));
        [Test] public void IsRegistered_EmptyId_False()         => Assert.IsFalse(_tray.IsRegistered(""));

        // ─── Register ──────────────────────────────────────────────────────────

        [Test]
        public void Register_NewId_IsRegisteredTrue()
        {
            _tray.Register("inv", null, null);
            Assert.IsTrue(_tray.IsRegistered("inv"));
        }

        [Test]
        public void Register_CreatesButtonGameObject()
        {
            _tray.Register("inv", null, null);
            Assert.IsTrue(FindButton("inv") != null);
        }

        [Test]
        public void Register_NullId_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _tray.Register(null, null, null));
            Assert.IsFalse(_tray.IsRegistered(null));
        }

        [Test]
        public void Register_EmptyId_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _tray.Register("", null, null));
            Assert.IsFalse(_tray.IsRegistered(""));
        }

        [Test]
        public void Register_SameIdTwice_DoesNotDuplicateButton()
        {
            _tray.Register("dup", null, null);
            _tray.Register("dup", null, null);
            Assert.AreEqual(1, CountButtons("dup"));
            Assert.AreEqual(1, _tray.Count);
        }

        [Test]
        public void Register_SameIdTwice_UpdatesSpriteAndCallback()
        {
            var s1 = NewWhiteSprite();
            var s2 = NewWhiteSprite();
            int callsA = 0, callsB = 0;
            _tray.Register("upd", s1, () => callsA++);
            _tray.Register("upd", s2, () => callsB++);

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
            _tray.Register("inv", null, null);
            _tray.Register("spellbar", null, null);
            Assert.AreEqual(1, CountButtons("inv"));
            Assert.AreEqual(1, CountButtons("spellbar"));
        }

        // ─── Unregister ────────────────────────────────────────────────────────

        [Test]
        public void Unregister_ExistingId_Removes()
        {
            _tray.Register("rem", null, null);
            _tray.Unregister("rem");
            Assert.IsFalse(_tray.IsRegistered("rem"));
            Assert.IsTrue(FindButton("rem") == null);
        }

        [Test]
        public void Unregister_NonExistentId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _tray.Unregister("nope"));

        [Test]
        public void Unregister_NullId_DoesNotThrow()
            => Assert.DoesNotThrow(() => _tray.Unregister(null));

        // ─── Click → onToggle (PERSISTENT — does NOT auto-unregister) ─────────

        [Test]
        public void Click_InvokesOnToggle()
        {
            int calls = 0;
            _tray.Register("clk", null, () => calls++);
            FindButton("clk").onClick.Invoke();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Click_DoesNOTUnregisterEntry()
        {
            // Critical contract: tray buttons are PERSISTENT.
            _tray.Register("clk", null, null);
            FindButton("clk").onClick.Invoke();
            Assert.IsTrue(_tray.IsRegistered("clk"),
                "Click must NOT unregister — button stays available for re-toggle");
            Assert.IsTrue(FindButton("clk") != null);
        }

        [Test]
        public void Click_TwiceFiresCallbackTwice()
        {
            int calls = 0;
            _tray.Register("toggle", null, () => calls++);
            var btn = FindButton("toggle");
            btn.onClick.Invoke();
            btn.onClick.Invoke();
            Assert.AreEqual(2, calls,
                "Persistent button must keep working — toggle on/off pattern");
        }

        [Test]
        public void Click_NullCallback_DoesNotThrow()
        {
            _tray.Register("safe", null, null);
            Assert.DoesNotThrow(() => FindButton("safe").onClick.Invoke());
        }

        [Test]
        public void Click_OtherEntriesUnaffected()
        {
            _tray.Register("a", null, null);
            _tray.Register("b", null, null);
            FindButton("a").onClick.Invoke();
            Assert.IsTrue(_tray.IsRegistered("a"));
            Assert.IsTrue(_tray.IsRegistered("b"));
        }

        // ─── Singleton lifecycle ───────────────────────────────────────────────

        [Test]
        public void Destroy_TrayGO_ClearsInstance()
        {
            Assert.IsTrue(MinimizedHUDTray.Instance != null);
            Object.DestroyImmediate(_trayGo);
            Assert.IsTrue(MinimizedHUDTray.Instance == null);
        }

        [Test]
        public void Duplicate_SecondComponent_InstanceRemainsFirst()
        {
            var dupeGo = new GameObject("DupeTray");
            _sceneObjects.Add(dupeGo);
            dupeGo.AddComponent<MinimizedHUDTray>();
            Assert.AreSame(_trayGo, MinimizedHUDTray.Instance.gameObject);
        }
    }
}
