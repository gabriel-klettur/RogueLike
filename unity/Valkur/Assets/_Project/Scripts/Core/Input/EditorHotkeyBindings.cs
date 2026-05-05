using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Single resolution point for every editor / quick-save / dev-overlay hotkey.
    ///
    /// In play mode, <see cref="InputService"/> is bootstrapped by
    /// <see cref="RuntimeInputBootstrap"/> and the canonical action from the
    /// <c>Editors</c> map is returned — so all editors share one binding source.
    ///
    /// In EditMode tests <see cref="InputService"/> is not initialized; the resolver
    /// falls back to a fresh ad-hoc <see cref="InputAction"/>. The <c>ownsAction</c>
    /// flag tells the caller whether to <see cref="InputAction.Dispose"/> on teardown.
    ///
    /// **Preferred runtime usage**: the stateless <see cref="WasPerformedThisFrame(Hotkey)"/>
    /// and <see cref="IsPressed(Hotkey)"/> overloads. They look up the live action from
    /// <see cref="InputService"/> on every call, so they are immune to the
    /// "zombie InputAction" hot-reload bug (Unity serializes a MonoBehaviour's
    /// <see cref="InputAction"/> field, the deserialized clone has <c>bindings.Count == 0</c>
    /// and <c>actionMap == null</c>, and the editor's stored reference goes silently dead
    /// while the canonical asset's action is still firing). Storing the result of
    /// <see cref="Resolve"/> in a field is now discouraged for runtime code.
    /// </summary>
    public static class EditorHotkeyBindings
    {
        public enum Hotkey
        {
            ToggleParticles,    // F1
            ToggleCombatRanges, // F2 (Alt)
            ToggleTimeWeather,  // F2 (bare)
            ToggleSpawner,      // F3
            ToggleLighting,     // F3 (Ctrl)
            ToggleSpells,       // F4
            ToggleEntities,     // F5
            ToggleInventory,    // F6
            ToggleItems,        // F7
            ToggleTile,         // F8
            ToggleDebugHUD,     // F9
            ToggleBuildings,    // F10
            ToggleMap,          // F11
            ToggleFSM,          // F12
            QuickSave,          // F5 (Ctrl)
            QuickLoad,          // F9 (Ctrl)
            CtrlModifier,       // leftCtrl
            AltModifier,        // leftAlt
            ToggleDevConsole,   // backquote
            OpenGeneralEditor,  // escape (top-level launcher panel)
        }

        public static InputAction Resolve(Hotkey hotkey, out bool ownsAction)
        {
            var svc = InputService.Instance;
            if (svc != null)
            {
                ownsAction = false;
                var action = Get(svc.Editors, hotkey);
                // Self-heal: a prior test (or hot-reload teardown that called
                // ResetForTests / disabled the canonical InputActionAsset
                // directly) may have left the Editors action map disabled.
                // The Editors map is documented as always-on — re-enable it
                // defensively so callers always receive a usable action.
                // Mirrors InputService.EnsureAlwaysOnMapsEnabled but applied
                // at every resolve to dodge sticky disabled state.
                if (action != null && action.actionMap != null && !action.actionMap.enabled)
                    action.actionMap.Enable();
                return action;
            }

            ownsAction = true;
            var ad = new InputAction(hotkey.ToString(), InputActionType.Button, FallbackPath(hotkey));
            ad.Enable();
            return ad;
        }

        // ── Stateless query API (preferred for runtime) ─────────────────────────
        //
        // Resolves the live canonical action on every call so caller code never
        // holds a stale reference. Eliminates the zombie-action class of bug.
        //
        // While a modal panel (chat / dev console) holds focus all hotkeys
        // are suppressed EXCEPT ToggleDevConsole — the ~ key must keep
        // working so the user can dismiss the dev console.

        public static bool WasPerformedThisFrame(Hotkey hotkey)
        {
            if (InputBlocker.IsGameplayBlocked && hotkey != Hotkey.ToggleDevConsole)
                return false;
            var a = ResolveLive(hotkey);
            bool newSystem = a != null && a.WasPerformedThisFrame();
            // Legacy fallback: under Unity 2022.3 in the Editor the new
            // InputSystem package intermittently drops OS event delivery,
            // and F-keys silently die. UnityEngine.Input always works as
            // long as activeInputHandler != "Input System Package only".
            return newSystem || LegacyKeyDown(hotkey);
        }

        public static bool IsPressed(Hotkey hotkey)
        {
            if (InputBlocker.IsGameplayBlocked && hotkey != Hotkey.ToggleDevConsole)
                return false;
            var a = ResolveLive(hotkey);
            bool newSystem = a != null && a.IsPressed();
            return newSystem || LegacyKeyHeld(hotkey);
        }

        public static bool WasReleasedThisFrame(Hotkey hotkey)
        {
            if (InputBlocker.IsGameplayBlocked && hotkey != Hotkey.ToggleDevConsole)
                return false;
            var a = ResolveLive(hotkey);
            bool newSystem = a != null && a.WasReleasedThisFrame();
            return newSystem || LegacyKeyUp(hotkey);
        }

        private static KeyCode LegacyKeyCode(Hotkey hotkey) => hotkey switch
        {
            Hotkey.ToggleParticles    => KeyCode.F1,
            Hotkey.ToggleCombatRanges => KeyCode.F2,
            Hotkey.ToggleTimeWeather  => KeyCode.F2,
            Hotkey.ToggleSpawner      => KeyCode.F3,
            Hotkey.ToggleLighting     => KeyCode.F3,
            Hotkey.ToggleSpells       => KeyCode.F4,
            Hotkey.ToggleEntities     => KeyCode.F5,
            Hotkey.ToggleInventory    => KeyCode.F6,
            Hotkey.ToggleItems        => KeyCode.F7,
            Hotkey.ToggleTile         => KeyCode.F8,
            Hotkey.ToggleDebugHUD     => KeyCode.F9,
            Hotkey.ToggleBuildings    => KeyCode.F10,
            Hotkey.ToggleMap          => KeyCode.F11,
            Hotkey.ToggleFSM          => KeyCode.F12,
            Hotkey.QuickSave          => KeyCode.F5,
            Hotkey.QuickLoad          => KeyCode.F9,
            Hotkey.CtrlModifier       => KeyCode.LeftControl,
            Hotkey.AltModifier        => KeyCode.LeftAlt,
            Hotkey.ToggleDevConsole   => KeyCode.BackQuote,
            Hotkey.OpenGeneralEditor  => KeyCode.Escape,
            _ => KeyCode.None
        };

        private static bool LegacyKeyDown(Hotkey hotkey) =>
            UnityEngine.Input.GetKeyDown(LegacyKeyCode(hotkey));
        private static bool LegacyKeyHeld(Hotkey hotkey) =>
            UnityEngine.Input.GetKey(LegacyKeyCode(hotkey));
        private static bool LegacyKeyUp(Hotkey hotkey) =>
            UnityEngine.Input.GetKeyUp(LegacyKeyCode(hotkey));

        /// <summary>
        /// Returns the canonical live action when InputService is up, or a cached
        /// ad-hoc action when running in EditMode tests. Stateless from the caller's
        /// perspective — they never need to dispose. The cache is reset on Play
        /// Mode entry so it cannot leak across sessions with Domain Reload off.
        /// </summary>
        private static InputAction ResolveLive(Hotkey hotkey)
        {
            var svc = InputService.Instance;
            if (svc != null) return Get(svc.Editors, hotkey);

            if (_adHocCache.TryGetValue(hotkey, out var cached) &&
                cached != null && cached.bindings.Count > 0)
                return cached;

            var fresh = new InputAction(hotkey.ToString(), InputActionType.Button, FallbackPath(hotkey));
            fresh.Enable();
            _adHocCache[hotkey] = fresh;
            return fresh;
        }

        private static readonly Dictionary<Hotkey, InputAction> _adHocCache =
            new Dictionary<Hotkey, InputAction>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAdHocCache() => _adHocCache.Clear();

        // ── Field-bound revival helper (for code that still stores InputAction) ─
        //
        // For MonoBehaviours that historically held a private InputAction field,
        // call this from Update(). It detects the "zombie after hot-reload" state
        // (action.bindings.Count == 0) and re-resolves from the canonical source.

        public static InputAction ReviveIfZombie(InputAction current, Hotkey hotkey, ref bool ownsAction)
        {
            if (current != null && current.bindings.Count > 0) return current;
            if (ownsAction && current != null) current.Dispose();
            return Resolve(hotkey, out ownsAction);
        }

        public static string FallbackPath(Hotkey hotkey) => hotkey switch
        {
            Hotkey.ToggleParticles    => "<Keyboard>/f1",
            Hotkey.ToggleCombatRanges => "<Keyboard>/f2",
            Hotkey.ToggleTimeWeather  => "<Keyboard>/f2",
            Hotkey.ToggleSpawner      => "<Keyboard>/f3",
            Hotkey.ToggleLighting     => "<Keyboard>/f3",
            Hotkey.ToggleSpells       => "<Keyboard>/f4",
            Hotkey.ToggleEntities     => "<Keyboard>/f5",
            Hotkey.ToggleInventory    => "<Keyboard>/f6",
            Hotkey.ToggleItems        => "<Keyboard>/f7",
            Hotkey.ToggleTile         => "<Keyboard>/f8",
            Hotkey.ToggleDebugHUD     => "<Keyboard>/f9",
            Hotkey.ToggleBuildings    => "<Keyboard>/f10",
            Hotkey.ToggleMap          => "<Keyboard>/f11",
            Hotkey.ToggleFSM          => "<Keyboard>/f12",
            Hotkey.QuickSave          => "<Keyboard>/f5",
            Hotkey.QuickLoad          => "<Keyboard>/f9",
            Hotkey.CtrlModifier       => "<Keyboard>/leftCtrl",
            Hotkey.AltModifier        => "<Keyboard>/leftAlt",
            Hotkey.ToggleDevConsole   => "<Keyboard>/backquote",
            Hotkey.OpenGeneralEditor  => "<Keyboard>/escape",
            _ => null
        };

        private static InputAction Get(InputService.EditorsActions e, Hotkey hotkey) => hotkey switch
        {
            Hotkey.ToggleParticles    => e.ToggleParticles,
            Hotkey.ToggleCombatRanges => e.ToggleCombatRanges,
            Hotkey.ToggleTimeWeather  => e.ToggleTimeWeather,
            Hotkey.ToggleSpawner      => e.ToggleSpawner,
            Hotkey.ToggleLighting     => e.ToggleLighting,
            Hotkey.ToggleSpells       => e.ToggleSpells,
            Hotkey.ToggleEntities     => e.ToggleEntities,
            Hotkey.ToggleInventory    => e.ToggleInventory,
            Hotkey.ToggleItems        => e.ToggleItems,
            Hotkey.ToggleTile         => e.ToggleTile,
            Hotkey.ToggleDebugHUD     => e.ToggleDebugHUD,
            Hotkey.ToggleBuildings    => e.ToggleBuildings,
            Hotkey.ToggleMap          => e.ToggleMap,
            Hotkey.ToggleFSM          => e.ToggleFSM,
            Hotkey.QuickSave          => e.QuickSave,
            Hotkey.QuickLoad          => e.QuickLoad,
            Hotkey.CtrlModifier       => e.CtrlModifier,
            Hotkey.AltModifier        => e.AltModifier,
            Hotkey.ToggleDevConsole   => e.ToggleDevConsole,
            Hotkey.OpenGeneralEditor  => e.OpenGeneralEditor,
            _ => null
        };
    }
}
