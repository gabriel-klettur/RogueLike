using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core.Input;

namespace Valkur.Tests.PlayMode.UI
{
    public class RuntimeMouseMenuInputPlayTests
    {
        private GameObject _canvasGo;
        private EventSystem _eventSystem;
        private Mouse _mouse;
        private readonly InputTestFixture _inputFixture = new InputTestFixture();
        private bool _previousRunInBackground;
        private InputSettings.BackgroundBehavior _previousBackgroundBehavior;
        private InputSettings.UpdateMode _previousUpdateMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousRunInBackground = Application.runInBackground;
            _previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            _previousUpdateMode = InputSystem.settings.updateMode;
            _inputFixture.Setup();
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;

            DestroyAllEventSystems();
            yield return null;

            _mouse = InputSystem.AddDevice<Mouse>("RuntimeMenuTestMouse");

            _eventSystem = RuntimeInputBootstrap.EnsureRuntimeInput();

            _canvasGo = new GameObject("RuntimeMouseMenuInputCanvas", typeof(RectTransform));
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasGo.AddComponent<GraphicRaycaster>();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_canvasGo != null)
                Object.Destroy(_canvasGo);
            if (_eventSystem != null)
                Object.Destroy(_eventSystem.gameObject);

            if (_mouse != null && _mouse.added)
                InputSystem.RemoveDevice(_mouse);

            Application.runInBackground = _previousRunInBackground;
            InputSystem.settings.backgroundBehavior = _previousBackgroundBehavior;
            InputSystem.settings.updateMode = _previousUpdateMode;
            _inputFixture.TearDown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RuntimeInputBootstrap_DispatchesMouseClickToRuntimeButton()
        {
            int clickCount = 0;
            Button button = CreateButton("ClickableMenuButton", () => clickCount++);

            var module = _eventSystem.GetComponent<InputSystemUIInputModule>();
            Assert.IsNotNull(module, "Menus must use InputSystemUIInputModule at runtime.");
            // Regression: ConfigureModule used to assign these references and THEN
            // disable the module, and InputSystemUIInputModule.OnDisable clears them —
            // so a freshly created [PersistentEventSystem] came up with no actionsAsset
            // and no point action at all. Runtime menus had no pointer pipeline.
            Assert.IsNotNull(module.actionsAsset,
                "The UI module must keep the canonical asset after ConfigureModule.");
            Assert.IsNotNull(module.point?.action, "Point action must be configured for runtime menu hover/click.");
            Assert.IsNotNull(module.leftClick?.action, "Left click action must be configured for runtime menu buttons.");

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertPointerRaycastsButton(screenCenter, button);

            yield return ProcessMouseFrame(module, screenCenter, pressed: false);
            yield return ProcessMouseFrame(module, screenCenter, pressed: true, assertPressed: true);
            yield return ProcessMouseFrame(module, screenCenter, pressed: false);

            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(1, clickCount,
                "A real runtime Button must receive a mouse click after InputDiagnostics.EnsureEventSystem().");
        }

        private Button CreateButton(string name, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGo = new GameObject(name, typeof(RectTransform));
            buttonGo.transform.SetParent(_canvasGo.transform, false);

            var rect = (RectTransform)buttonGo.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(240f, 96f);

            var image = buttonGo.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            return button;
        }

        private IEnumerator ProcessMouseFrame(InputSystemUIInputModule module, Vector2 position, bool pressed, bool assertPressed = false)
        {
            QueueMouse(position, pressed);
            if (assertPressed)
            {
                Assert.IsTrue(_mouse.leftButton.isPressed, "Queued mouse state must press the real Mouse.leftButton control.");
                Assert.IsTrue(module.leftClick.action.IsPressed(), "Runtime UI leftClick action must read the mouse press.");
            }

            _eventSystem.UpdateModules();
            module.Process();
            yield return null;
        }

        private void QueueMouse(Vector2 position, bool pressed)
        {
            _inputFixture.Move(_mouse.position, position, queueEventOnly: true);
            if (pressed)
                _inputFixture.Press(_mouse.leftButton, queueEventOnly: true);
            else
                _inputFixture.Release(_mouse.leftButton, queueEventOnly: true);

            InputSystem.Update();
        }

        private void AssertPointerRaycastsButton(Vector2 position, Button button)
        {
            var eventData = new PointerEventData(_eventSystem) { position = position };
            var results = new List<RaycastResult>();
            _eventSystem.RaycastAll(eventData, results);

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject == button.gameObject)
                    return;
            }

            Assert.Fail($"Runtime mouse test pointer did not raycast the menu button at {position}. Hits: {string.Join(", ", results.ConvertAll(r => r.gameObject.name))}");
        }

        private static void DestroyAllEventSystems()
        {
            var systems = Object.FindObjectsOfType<EventSystem>();
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null)
                    Object.Destroy(systems[i].gameObject);
            }
        }
    }
}
