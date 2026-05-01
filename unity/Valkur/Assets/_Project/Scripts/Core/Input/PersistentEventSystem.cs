using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Valkur.Core.Input
{
    /// <summary>
    /// A scene-independent <see cref="EventSystem"/> wired to <see cref="InputService"/>.
    /// Created at <c>BeforeSceneLoad</c> with <c>DontDestroyOnLoad</c> so every scene
    /// inherits a working pointer-input pipeline without serializing its own EventSystem.
    ///
    /// This is the structural fix for the "MainMenu.unity has fileID:0 action refs"
    /// regression: scenes never serialize the InputSystemUIInputModule's action
    /// references, so the bindings cannot drift on save.
    /// </summary>
    public static class PersistentEventSystem
    {
        private static EventSystem _instance;

        public static EventSystem Instance => _instance;

        public static EventSystem Ensure()
        {
            // First-time setup: prefer adopting a scene-shipped EventSystem so we
            // don't lose any inspector tweaks (drag threshold, etc.); otherwise
            // build a fresh persistent one.
            if (_instance == null)
            {
                var existing = Object.FindObjectOfType<EventSystem>();
                if (existing != null)
                {
                    _instance = existing;
                    if (Application.isPlaying)
                        Object.DontDestroyOnLoad(_instance.gameObject);
                }
                else
                {
                    var go = new GameObject("[PersistentEventSystem]");
                    if (Application.isPlaying)
                        Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<EventSystem>();
                }
            }

            // Always reconfigure on every call. Subsequent scene loads ship their
            // own EventSystem GameObjects; ConfigureModule rewires our InputService
            // bindings AND deletes any duplicate EventSystem found in the scene,
            // guaranteeing a single, correctly-wired pointer-input pipeline.
            ConfigureModule(_instance);
            return _instance;
        }

        public static void ConfigureModule(EventSystem eventSystem)
        {
            if (eventSystem == null) return;

            var legacy = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                if (Application.isPlaying) Object.Destroy(legacy);
                else                       Object.DestroyImmediate(legacy);
            }

            var module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null)
                module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            // Wire from InputService — pure code, no scene-asset references.
            // Toggle enabled around the rebind so OnEnable re-subscribes against
            // the freshly-bound actions (Unity 2022.3 caches subscriptions in OnEnable).
            var ui = InputService.Instance?.UI;
            if (ui != null)
            {
                bool wasEnabled = module.enabled;
                if (wasEnabled) module.enabled = false;

                module.actionsAsset = InputService.Instance.Asset;
                module.point        = InputActionReference.Create(ui.Point);
                module.leftClick    = InputActionReference.Create(ui.Click);
                module.rightClick   = InputActionReference.Create(ui.RightClick);
                module.middleClick  = InputActionReference.Create(ui.MiddleClick);
                module.scrollWheel  = InputActionReference.Create(ui.ScrollWheel);
                module.move         = InputActionReference.Create(ui.Navigate);
                module.submit       = InputActionReference.Create(ui.Submit);
                module.cancel       = InputActionReference.Create(ui.Cancel);

                if (wasEnabled) module.enabled = true;
            }

            module.enabled = true;
            eventSystem.enabled = true;
            RemoveDuplicates(eventSystem);
        }

        private static void RemoveDuplicates(EventSystem keep)
        {
            var all = Object.FindObjectsOfType<EventSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == keep) continue;
                if (Application.isPlaying) Object.Destroy(all[i].gameObject);
                else                       Object.DestroyImmediate(all[i].gameObject);
            }
        }

        /// <summary>Test hook: drop the singleton so a fresh one can be created.
        /// Public so EditMode tests in a sibling assembly can call it.</summary>
        public static void ResetForTests()
        {
            if (_instance != null && _instance.gameObject != null)
            {
                if (Application.isPlaying) Object.Destroy(_instance.gameObject);
                else                       Object.DestroyImmediate(_instance.gameObject);
            }
            _instance = null;
        }
    }
}
