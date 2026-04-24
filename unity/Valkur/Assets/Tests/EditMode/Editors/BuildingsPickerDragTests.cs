using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Buildings;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Tests for the drag-from-picker feature in <see cref="BuildingsRuntimeEditor"/>.
    ///
    /// Mirrors Python <c>building_picker_controller.start_drag</c> and
    /// <c>building_picker_view._draw_drag_preview</c> — pressing LMB on a slot in the
    /// Buildings panel and dragging onto the map places that template directly,
    /// with a semi-transparent ghost following the cursor.
    ///
    /// Coverage:
    ///   • Field & method existence (all private — verified via reflection).
    ///   • Drag-threshold constant value (8 px).
    ///   • Default state (idle, templateId = -1, ghost null).
    ///   • OnPickerSlotPointerDown stores templateId and start screen position.
    ///   • CancelPickerDrag fully resets state and hides ghost.
    ///   • BuildDragGhost creates the ghost GameObject with the expected
    ///     RectTransform / Image / CanvasGroup configuration, is idempotent,
    ///     and starts inactive.
    ///   • UpdatePickerDrag is safe to invoke when no Mouse.current is bound
    ///     (EditMode environment) and does not throw.
    /// </summary>
    [TestFixture]
    public class BuildingsPickerDragTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // ── reflection helpers ────────────────────────────────────────────────────

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

        private static FieldInfo GetStaticField(Type type, string name)
        {
            var t = type;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static MethodInfo GetMethod(Type type, string name, Type[] paramTypes = null)
        {
            var t = type;
            while (t != null)
            {
                var m = paramTypes == null
                    ? t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    : t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                                  null, paramTypes, null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private BuildingsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            LogAssert.ignoreFailingMessages = true;
            var go   = new GameObject("TestBuildingsEditor");
            var ed   = go.AddComponent<BuildingsRuntimeEditor>();
            // Ensure OnSingletonAwake ran (matches pattern used in BuildingsEditorLifecycleTests)
            var toggle = GetField(ed, "_toggleAction");
            if (toggle?.GetValue(ed) == null)
            {
                var awake = GetMethod(typeof(BuildingsRuntimeEditor), "OnSingletonAwake");
                awake?.Invoke(ed, null);
            }
            _scene.Add(go);
            return ed;
        }

        private Canvas CreateCanvas()
        {
            var go     = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _scene.Add(go);
            return canvas;
        }

        private BuildingTemplateData MakeTemplate(int id)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = id;
            t.colliderScope = "CG";
            _assets.Add(t);
            return t;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var so in _assets) if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 1. Field & method existence (compile-time contract)
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void Fields_AllPickerDragFields_Exist()
        {
            var t = typeof(BuildingsRuntimeEditor);
            Assert.IsNotNull(GetStaticField(t, "PICKER_DRAG_THRESHOLD"),
                "PICKER_DRAG_THRESHOLD constant must exist (drag activation threshold).");

            // Instance fields verified via a temporary editor.
            var ed = CreateEditor();
            Assert.IsNotNull(GetField(ed, "_pickerDragging"),       "_pickerDragging must exist.");
            Assert.IsNotNull(GetField(ed, "_pickerDragTemplateId"), "_pickerDragTemplateId must exist.");
            Assert.IsNotNull(GetField(ed, "_pickerDragStartScreen"), "_pickerDragStartScreen must exist.");
            Assert.IsNotNull(GetField(ed, "_dragGhostGo"),  "_dragGhostGo must exist.");
            Assert.IsNotNull(GetField(ed, "_dragGhostRt"),  "_dragGhostRt must exist.");
            Assert.IsNotNull(GetField(ed, "_dragGhostImg"), "_dragGhostImg must exist.");
        }

        [Test]
        public void Methods_AllPickerDragMethods_Exist()
        {
            var t = typeof(BuildingsRuntimeEditor);
            Assert.IsNotNull(GetMethod(t, "BuildDragGhost"),
                "BuildDragGhost() must exist.");
            Assert.IsNotNull(GetMethod(t, "OnPickerSlotPointerDown", new[] { typeof(int) }),
                "OnPickerSlotPointerDown(int) must exist.");
            Assert.IsNotNull(GetMethod(t, "UpdatePickerDrag"),
                "UpdatePickerDrag() must exist (called from Update).");
            Assert.IsNotNull(GetMethod(t, "CancelPickerDrag"),
                "CancelPickerDrag() must exist (called from Deactivate).");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 2. Drag-threshold constant
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void PickerDragThreshold_Equals_8Pixels()
        {
            // Python parity: 8-pixel cushion before a click becomes a drag — prevents
            // accidental drags on simple selection clicks.
            var f = GetStaticField(typeof(BuildingsRuntimeEditor), "PICKER_DRAG_THRESHOLD");
            Assert.IsNotNull(f);
            float v = (float) f.GetValue(null);
            Assert.AreEqual(8f, v, 0.0001f,
                "PICKER_DRAG_THRESHOLD should be 8 pixels (matches Python click-vs-drag heuristic).");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 3. Default state
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void DefaultState_PickerDragging_IsFalse()
        {
            var ed = CreateEditor();
            Assert.IsFalse((bool) GetField(ed, "_pickerDragging").GetValue(ed),
                "Drag must start idle.");
        }

        [Test]
        public void DefaultState_PickerDragTemplateId_IsMinusOne()
        {
            var ed = CreateEditor();
            Assert.AreEqual(-1, (int) GetField(ed, "_pickerDragTemplateId").GetValue(ed),
                "_pickerDragTemplateId must default to -1 (no slot armed).");
        }

        [Test]
        public void DefaultState_GhostObjects_AreNull()
        {
            var ed = CreateEditor();
            Assert.IsNull(GetField(ed, "_dragGhostGo").GetValue(ed),
                "Ghost GameObject is created lazily — must be null before first drag.");
            Assert.IsNull(GetField(ed, "_dragGhostRt").GetValue(ed),
                "Ghost RectTransform must be null before first drag.");
            Assert.IsNull(GetField(ed, "_dragGhostImg").GetValue(ed),
                "Ghost Image must be null before first drag.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 4. OnPickerSlotPointerDown
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void OnPickerSlotPointerDown_StoresTemplateId()
        {
            var ed     = CreateEditor();
            var method = GetMethod(typeof(BuildingsRuntimeEditor),
                "OnPickerSlotPointerDown", new[] { typeof(int) });

            method.Invoke(ed, new object[] { 42 });

            Assert.AreEqual(42, (int) GetField(ed, "_pickerDragTemplateId").GetValue(ed),
                "OnPickerSlotPointerDown must store the templateId for the future drag.");
            // _pickerDragging stays false until the threshold is crossed in UpdatePickerDrag.
            Assert.IsFalse((bool) GetField(ed, "_pickerDragging").GetValue(ed),
                "PointerDown must NOT immediately activate dragging — only arms the candidate.");
        }

        [Test]
        public void OnPickerSlotPointerDown_DoesNotThrow_WhenMouseUnavailable()
        {
            // EditMode tests have no Mouse.current — the implementation must
            // tolerate this via the `Mouse.current?` null-conditional.
            var ed     = CreateEditor();
            var method = GetMethod(typeof(BuildingsRuntimeEditor),
                "OnPickerSlotPointerDown", new[] { typeof(int) });

            Assert.DoesNotThrow(() => method.Invoke(ed, new object[] { 5 }),
                "OnPickerSlotPointerDown must be safe to invoke when Mouse.current is null.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 5. CancelPickerDrag
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void CancelPickerDrag_ResetsAllState()
        {
            var ed = CreateEditor();
            // Force "dragging" state.
            GetField(ed, "_pickerDragging").SetValue(ed, true);
            GetField(ed, "_pickerDragTemplateId").SetValue(ed, 99);

            GetMethod(typeof(BuildingsRuntimeEditor), "CancelPickerDrag").Invoke(ed, null);

            Assert.IsFalse((bool) GetField(ed, "_pickerDragging").GetValue(ed),
                "CancelPickerDrag must clear _pickerDragging.");
            Assert.AreEqual(-1, (int) GetField(ed, "_pickerDragTemplateId").GetValue(ed),
                "CancelPickerDrag must reset _pickerDragTemplateId to -1.");
        }

        [Test]
        public void CancelPickerDrag_HidesExistingGhost()
        {
            var ed = CreateEditor();
            // Inject a fake ghost GameObject.
            var ghost = new GameObject("FakeGhost"); _scene.Add(ghost);
            ghost.SetActive(true);
            GetField(ed, "_dragGhostGo").SetValue(ed, ghost);

            GetMethod(typeof(BuildingsRuntimeEditor), "CancelPickerDrag").Invoke(ed, null);

            Assert.IsFalse(ghost.activeSelf,
                "CancelPickerDrag must SetActive(false) on the existing ghost (preserves the GO for reuse).");
        }

        [Test]
        public void CancelPickerDrag_NoGhost_DoesNotThrow()
        {
            var ed = CreateEditor();
            // _dragGhostGo stays null — must not NRE.
            Assert.DoesNotThrow(
                () => GetMethod(typeof(BuildingsRuntimeEditor), "CancelPickerDrag").Invoke(ed, null),
                "CancelPickerDrag must be safe to call even when ghost was never built.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 6. BuildDragGhost
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void BuildDragGhost_CreatesGhost_WithExpectedComponents()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var ghostGo  = (GameObject)    GetField(ed, "_dragGhostGo").GetValue(ed);
            var ghostRt  = (RectTransform) GetField(ed, "_dragGhostRt").GetValue(ed);
            var ghostImg = (Image)         GetField(ed, "_dragGhostImg").GetValue(ed);

            Assert.IsNotNull(ghostGo,  "BuildDragGhost must create the ghost GameObject.");
            Assert.IsNotNull(ghostRt,  "BuildDragGhost must capture the RectTransform.");
            Assert.IsNotNull(ghostImg, "BuildDragGhost must add and capture the Image.");

            Assert.IsNotNull(ghostGo.GetComponent<CanvasGroup>(),
                "BuildDragGhost must add a CanvasGroup so raycasts pass through.");
        }

        [Test]
        public void BuildDragGhost_StartsInactive()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var ghostGo = (GameObject) GetField(ed, "_dragGhostGo").GetValue(ed);
            Assert.IsFalse(ghostGo.activeSelf,
                "Ghost must be created hidden — only shown after the drag threshold is crossed.");
        }

        [Test]
        public void BuildDragGhost_ParentedToCanvas()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var ghostGo = (GameObject) GetField(ed, "_dragGhostGo").GetValue(ed);
            Assert.AreSame(canvas.transform, ghostGo.transform.parent,
                "Ghost must be parented to the editor canvas so it follows the screen-overlay.");
        }

        [Test]
        public void BuildDragGhost_SizeIs80x80()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var rt = (RectTransform) GetField(ed, "_dragGhostRt").GetValue(ed);
            Assert.AreEqual(80f, rt.sizeDelta.x, 0.001f, "Ghost width must match slot size (80 px).");
            Assert.AreEqual(80f, rt.sizeDelta.y, 0.001f, "Ghost height must match slot size (80 px).");
        }

        [Test]
        public void BuildDragGhost_PivotIsCentered()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var rt = (RectTransform) GetField(ed, "_dragGhostRt").GetValue(ed);
            Assert.AreEqual(0.5f, rt.pivot.x, 0.001f, "Ghost pivot.x must be 0.5 (cursor centered).");
            Assert.AreEqual(0.5f, rt.pivot.y, 0.001f, "Ghost pivot.y must be 0.5 (cursor centered).");
        }

        [Test]
        public void BuildDragGhost_ImageIsSemiTransparent_70PercentAlpha()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var img = (Image) GetField(ed, "_dragGhostImg").GetValue(ed);
            Assert.AreEqual(0.70f, img.color.a, 0.01f,
                "Ghost image alpha must be 0.70 (70%) — preview is intentionally semi-transparent.");
            Assert.IsFalse(img.raycastTarget,
                "Ghost image must NOT be a raycast target (drag-drop must hit the map underneath).");
            Assert.IsTrue(img.preserveAspect,
                "Ghost image must preserve aspect to avoid stretching the slot sprite.");
        }

        [Test]
        public void BuildDragGhost_CanvasGroup_DoesNotBlockRaycasts()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);

            GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost").Invoke(ed, null);

            var ghostGo = (GameObject) GetField(ed, "_dragGhostGo").GetValue(ed);
            var cg      = ghostGo.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg, "CanvasGroup must be present.");
            Assert.IsFalse(cg.blocksRaycasts,
                "CanvasGroup.blocksRaycasts must be false — drag-drop targets the map, not the ghost.");
        }

        [Test]
        public void BuildDragGhost_IsIdempotent()
        {
            var ed     = CreateEditor();
            var canvas = CreateCanvas();
            GetField(ed, "_canvas").SetValue(ed, canvas);
            var build  = GetMethod(typeof(BuildingsRuntimeEditor), "BuildDragGhost");

            build.Invoke(ed, null);
            var firstGhost = (GameObject) GetField(ed, "_dragGhostGo").GetValue(ed);
            build.Invoke(ed, null);
            var secondGhost = (GameObject) GetField(ed, "_dragGhostGo").GetValue(ed);

            Assert.AreSame(firstGhost, secondGhost,
                "BuildDragGhost must be idempotent — second call must not create a new GameObject.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 7. UpdatePickerDrag — safety / no-op paths
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void UpdatePickerDrag_NoMouse_DoesNotThrow()
        {
            // EditMode environment has no active Mouse device — UpdatePickerDrag
            // must early-return safely (matches `if (mouse == null) return;`).
            var ed = CreateEditor();
            Assert.DoesNotThrow(
                () => GetMethod(typeof(BuildingsRuntimeEditor), "UpdatePickerDrag").Invoke(ed, null),
                "UpdatePickerDrag must be safe to invoke when Mouse.current is null.");
        }

        [Test]
        public void UpdatePickerDrag_LMBNotPressed_DisarmsPendingTemplate()
        {
            // When a slot is armed (_pickerDragTemplateId >= 0) but LMB is not held
            // (the user already released — handled as a normal click), Phase 1 must
            // disarm the candidate so a stale id never starts a drag on the next press.
            var ed = CreateEditor();
            GetField(ed, "_pickerDragTemplateId").SetValue(ed, 7);
            GetField(ed, "_pickerDragging").SetValue(ed, false);

            GetMethod(typeof(BuildingsRuntimeEditor), "UpdatePickerDrag").Invoke(ed, null);

            Assert.AreEqual(-1, (int) GetField(ed, "_pickerDragTemplateId").GetValue(ed),
                "When LMB is not pressed, UpdatePickerDrag must disarm the pending template " +
                "(prevents stale state from triggering a drag on the next pointer down).");
            Assert.IsFalse((bool) GetField(ed, "_pickerDragging").GetValue(ed),
                "Dragging must remain false — release without crossing the threshold is a normal click.");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 8. EventTrigger contract (used by RefreshPicker to wire each slot)
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void EventTriggerEntry_PointerDown_IsSupported()
        {
            // Sanity-check that the EventSystems API used by RefreshPicker
            // (EventTrigger + EventTriggerType.PointerDown) is available.
            var go = new GameObject("Probe", typeof(RectTransform));
            _scene.Add(go);
            var et    = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            et.triggers.Add(entry);
            Assert.AreEqual(1, et.triggers.Count);
            Assert.AreEqual(EventTriggerType.PointerDown, et.triggers[0].eventID);
        }
    }
}
