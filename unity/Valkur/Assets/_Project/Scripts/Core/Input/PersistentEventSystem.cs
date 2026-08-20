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

        public static EventSystem Ensure() => Ensure(createIfMissing: true);

        /// <summary>
        /// <paramref name="createIfMissing"/> must be FALSE when called before the
        /// first scene's objects have awoken (<c>BeforeSceneLoad</c>). Minting an
        /// EventSystem there guarantees a duplicate the moment a scene that ships
        /// its own (MainMenu) runs <c>OnEnable</c>, and uGUI answers that with
        /// "There can be only one active Event System." — once per boot. Deferring
        /// to the <c>sceneLoaded</c> pass lets us adopt the scene's instead.
        /// </summary>
        public static EventSystem Ensure(bool createIfMissing)
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
                    if (!createIfMissing) return null;
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

            // The new InputSystemUIInputModule cannot deliver clicks to Button
            // handlers when the InputSystem package's OS event pipeline is
            // dropping events (a known recurring problem in Unity 2022.3.62f1
            // Editor — Mouse.current.position stays at (0,0) even when the
            // user clicks). The legacy StandaloneInputModule reads from
            // UnityEngine.Input which never breaks that way, so we install
            // BOTH modules: the new one stays enabled in case the new pipeline
            // recovers, but Standalone takes precedence and guarantees clicks
            // always reach UI Button.OnClick handlers.

            // Ensure StandaloneInputModule (legacy) is present and enabled — this
            // is the one that actually delivers clicks reliably under the bug.
            var legacy = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacy == null)
                legacy = eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            legacy.enabled = true;

            // Also install the InputSystemUIInputModule for parity with the
            // canonical asset; it stays disabled because two enabled UI input
            // modules on one EventSystem fight over the same events. If we
            // ever fully fix the new pipeline we can enable it instead.
            var newModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (newModule == null)
                newModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            var ui = InputService.Instance?.UI;
            if (ui != null)
            {
                newModule.actionsAsset = InputService.Instance.Asset;
                newModule.point        = InputActionReference.Create(ui.Point);
                newModule.leftClick    = InputActionReference.Create(ui.Click);
                newModule.rightClick   = InputActionReference.Create(ui.RightClick);
                newModule.middleClick  = InputActionReference.Create(ui.MiddleClick);
                newModule.scrollWheel  = InputActionReference.Create(ui.ScrollWheel);
                newModule.move         = InputActionReference.Create(ui.Navigate);
                newModule.submit       = InputActionReference.Create(ui.Submit);
                newModule.cancel       = InputActionReference.Create(ui.Cancel);
            }
            newModule.enabled = false;

            // Order matters: drop scene-shipped EventSystems FIRST so their OnDisable
            // removes them from EventSystem.m_EventSystems before we (re)enable the
            // persistent one. Re-enabling first would briefly leave both in the list
            // and Unity would log "There can be only one active Event System." even
            // though we destroy the duplicate one frame later.
            RemoveDuplicates(eventSystem);
            eventSystem.enabled = true;
        }

        /// <summary>
        /// Disable the persistent EventSystem so its <c>OnDisable</c> removes it
        /// from <c>EventSystem.m_EventSystems</c>. Call this BEFORE activating a
        /// scene that ships its own EventSystem to avoid the "There can be only one
        /// active Event System." LogError that fires when both components are alive
        /// simultaneously. <see cref="Ensure"/> re-enables the persistent one after
        /// destroying scene duplicates.
        /// </summary>
        public static void Pause()
        {
            if (_instance != null) _instance.enabled = false;
        }

        private static void RemoveDuplicates(EventSystem keep)
        {
            var all = Object.FindObjectsOfType<EventSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == keep) continue;

                // Disable BEFORE destroying. Object.Destroy is deferred to the end of
                // the frame, so the duplicate would still be registered with
                // UIElementsRuntimeUtility when the caller re-enables the persistent
                // one on the next line — which is exactly the "only one active Event
                // System" error this sweep exists to prevent. Disabling runs OnDisable
                // synchronously and unregisters it now.
                all[i].enabled = false;

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
