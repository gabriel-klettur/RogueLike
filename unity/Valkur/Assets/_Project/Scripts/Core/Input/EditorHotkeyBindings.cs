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
            // THE FOURTEEN EDITOR TOGGLES SHIP UNBOUND. Every editor is reached from the
            // General Editor (Escape); the F-row is free. The actions stay declared so the
            // Controls editor can offer them as "sin asignar" and a player who wants F8 back
            // can put it there — deleting them would make that impossible, and the whole
            // point of the binding layer is that this is a preference rather than a fact
            // baked into source.
            ToggleParticles,
            ToggleCombatRanges,
            ToggleTimeWeather,
            ToggleSpawner,
            ToggleLighting,
            ToggleSpells,
            ToggleEntities,
            ToggleInventory,
            ToggleItems,
            ToggleTile,
            ToggleDebugHUD,
            ToggleBuildings,
            ToggleMap,
            ToggleFSM,

            // Still bound: these are not editors.
            QuickSave,          // Ctrl+F5
            QuickLoad,          // Ctrl+F9
            CtrlModifier,       // leftCtrl
            AltModifier,        // leftAlt
            ToggleDevConsole,   // backquote
            OpenGeneralEditor,  // escape — the way in to everything above
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

            var path = FallbackPath(hotkey);
            if (path == null)
            {
                // Ships unbound. Returning null rather than an action with no bindings keeps
                // "has no key" one answer instead of two that behave the same but read
                // differently at the call sites.
                ownsAction = false;
                return null;
            }

            ownsAction = true;
            var ad = new InputAction(hotkey.ToString(), InputActionType.Button, path);
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
            // Both backends, and the legacy half is DERIVED from whatever the hotkey is
            // bound to right now. It used to come from a hardcoded KeyCode table beside the
            // enum, which meant two things: a rebind moved only the InputSystem half, and —
            // the reason this mattered the day the editor F-keys were retired — clearing a
            // binding did not clear the key, because UnityEngine.Input.GetKeyDown(F8) went on
            // answering forever.
            return InputBindingResolver.WasPerformedThisFrame(ResolveLive(hotkey));
        }

        public static bool IsPressed(Hotkey hotkey)
        {
            if (InputBlocker.IsGameplayBlocked && hotkey != Hotkey.ToggleDevConsole)
                return false;
            return InputBindingResolver.IsPressed(ResolveLive(hotkey));
        }

        public static bool WasReleasedThisFrame(Hotkey hotkey)
        {
            if (InputBlocker.IsGameplayBlocked && hotkey != Hotkey.ToggleDevConsole)
                return false;
            return InputBindingResolver.WasReleasedThisFrame(ResolveLive(hotkey));
        }

        // The LegacyKeyCode table and its LegacyKeyDown / LegacyKeyHeld / LegacyKeyUp
        // helpers are GONE. They mapped each Hotkey to a literal KeyCode and fed
        // UnityEngine.Input directly, so the OR-gate's legacy leg answered for F1-F12 no
        // matter what the asset said — a rebind moved half of a hotkey, and REMOVING a
        // binding removed none of it. InputBindingResolver derives that half from the live
        // binding instead, which is what lets the editor F-keys be retired by clearing
        // bindings rather than by deleting code.

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

            var path = FallbackPath(hotkey);
            if (path == null) return null;

            var fresh = new InputAction(hotkey.ToString(), InputActionType.Button, path);
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

        /// <summary>
        /// The path an ad-hoc action gets when <see cref="InputService"/> is absent — EditMode
        /// tests, and nothing else. It MIRRORS the shipped asset, so the fourteen editor
        /// toggles answer null: they ship unbound, and a fallback that quietly re-bound them
        /// to F1-F12 in tests would make the suite disagree with the game about which keys
        /// exist. <see cref="ResolveLive"/> and <see cref="Resolve"/> both handle null by
        /// returning no action, which reads as "this hotkey has no key" everywhere.
        /// </summary>
        public static string FallbackPath(Hotkey hotkey) => hotkey switch
        {
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
