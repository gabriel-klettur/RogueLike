using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Brings the input pipeline up before any scene loads:
    ///   1. Applies <see cref="InputSystemConfigurator"/> (focus settings, dedup,
    ///      canRunInBackground flag) so devices and actions can never silently die
    ///      when the Game View loses focus.
    ///   2. Boots <see cref="InputService"/> from the canonical asset.
    ///   3. Creates a persistent <see cref="EventSystem"/> wired to InputService.UI.
    ///
    /// Subsequent scene loads reconfigure the persistent EventSystem so any scene that
    /// still ships its own EventSystem (legacy) is collapsed into a single instance.
    /// </summary>
    public static class RuntimeInputBootstrap
    {
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_subscribed)
                SceneManager.sceneLoaded -= OnSceneLoaded;
            _subscribed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // createEventSystemIfMissing: false — the first scene's objects have not
            // awoken yet, so creating one here means two active EventSystems the
            // instant a scene that ships its own (MainMenu) enables it. OnSceneLoaded
            // adopts the scene's, or creates one when the scene ships none.
            EnsureRuntimeInput(createEventSystemIfMissing: false);

            if (_subscribed) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }

        public static EventSystem EnsureRuntimeInput() => EnsureRuntimeInput(true);

        public static EventSystem EnsureRuntimeInput(bool createEventSystemIfMissing)
        {
            // Apply the boot-time fix-ups: dedup duplicate devices, flip
            // canRunInBackground bits, pin runtime InputSettings + force
            // m_HasFocus=true. See InputSystemConfigurator XML doc.
            InputSystemConfigurator.Apply();
            InputService.Initialize();

            // The player's saved controls: binding overrides and per-action stance masks, in
            // one document. This replaced EditorBindingsApplier, which bridged twelve editor
            // F-keys out of GameSettings and nothing else — every gameplay field in that file
            // (moveUpKeyA, dashKeyA, spell1KeyA.., primaryAttackMouse) had zero readers in
            // production, so the Controls panel let a player rebind their movement and changed
            // nothing at all. One model now, and it is the asset.
            //
            // Applied on every scene load as well as at boot, because the canonical asset
            // survives Domain Reload off and a scene transition is the cheapest place to
            // notice it has drifted.
            InputBindingStore.Apply();
            InputBindingResolver.Invalidate();

            var es = PersistentEventSystem.Ensure(createEventSystemIfMissing);
            // Pin m_HasFocus=true on every frame so Editor focus changes
            // (Console / Inspector / MCP / OS) cannot mute OS event delivery
            // to the InputSystem. Play-Mode-only.
            if (Application.isPlaying)
                InputFocusKeepalive.Ensure();
            return es;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Re-ensure on every scene load so legacy scenes that still ship an
            // EventSystem get collapsed into the persistent one. The configurator's
            // duplicate sweep also re-runs here, catching any device-add events that
            // happened between scene transitions.
            EnsureRuntimeInput();
        }
    }
}
