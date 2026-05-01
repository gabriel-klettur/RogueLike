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
    /// </summary>
    public static class EditorHotkeyBindings
    {
        public enum Hotkey
        {
            ToggleParticles,    // F1
            ToggleCombatRanges, // F2
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
        }

        public static InputAction Resolve(Hotkey hotkey, out bool ownsAction)
        {
            var svc = InputService.Instance;
            if (svc != null)
            {
                ownsAction = false;
                return Get(svc.Editors, hotkey);
            }

            ownsAction = true;
            var action = new InputAction(hotkey.ToString(), InputActionType.Button, FallbackPath(hotkey));
            action.Enable();
            return action;
        }

        public static string FallbackPath(Hotkey hotkey) => hotkey switch
        {
            Hotkey.ToggleParticles    => "<Keyboard>/f1",
            Hotkey.ToggleCombatRanges => "<Keyboard>/f2",
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
            _ => null
        };

        private static InputAction Get(InputService.EditorsActions e, Hotkey hotkey) => hotkey switch
        {
            Hotkey.ToggleParticles    => e.ToggleParticles,
            Hotkey.ToggleCombatRanges => e.ToggleCombatRanges,
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
            _ => null
        };
    }
}
