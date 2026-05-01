using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Single source of truth for every input binding in Valkur.
    ///
    /// Loads <c>Resources/Input/ValkurInputActions</c> once at runtime, instantiates a
    /// private copy so per-session enable/disable does not mutate the shared asset, and
    /// exposes typed accessors for the three action maps:
    ///   • <see cref="UI"/>       — pointer + navigation actions consumed by EventSystem and menus.
    ///   • <see cref="Gameplay"/> — player actions (Move, Look, PrimaryAttack, …).
    ///   • <see cref="Editors"/>  — F1–F12 hotkeys for in-game editors and quick save/load.
    ///
    /// Lifetime: process-wide singleton, bootstrapped before any scene loads via
    /// <see cref="RuntimeInputBootstrap"/>. Editors and UI screens query this service
    /// instead of constructing their own <see cref="InputAction"/> instances, eliminating
    /// the duplicate-binding / scene-asset-fragility class of bug.
    /// </summary>
    public sealed class InputService
    {
        public const string CanonicalAssetResourcePath = "Input/ValkurInputActions";

        private static InputService _instance;

        public static InputService Instance => _instance;
        public static bool HasInstance => _instance != null;

        public InputActionAsset Asset { get; }
        public UIActions UI { get; }
        public GameplayActions Gameplay { get; }
        public EditorsActions Editors { get; }

        private InputService(InputActionAsset asset)
        {
            Asset = asset;
            UI       = new UIActions(asset.FindActionMap("UI", throwIfNotFound: true));
            Gameplay = new GameplayActions(asset.FindActionMap("Gameplay", throwIfNotFound: true));
            Editors  = new EditorsActions(asset.FindActionMap("Editors", throwIfNotFound: true));

            // UI + Editors are always-on (mouse over menus, F-keys for editor toggles).
            // Gameplay is enabled by gameplay-scene bootstrap and disabled by pause/menus.
            UI.Map.Enable();
            Editors.Map.Enable();
        }

        /// <summary>
        /// Reset statics so each Play Mode entry starts from a clean slate. Required
        /// because Domain Reload is OFF in Valkur — without this the previous session's
        /// <see cref="_instance"/> would carry over (along with whatever map-enabled
        /// state the prior session ended in) and <see cref="Initialize"/> would skip
        /// rebuilding, leaving the canonical asset's maps possibly disabled.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _instance = null;
        }

        /// <summary>
        /// Bootstrap the service. Idempotent; safe to call from multiple places (the
        /// runtime bootstrap and EditMode tests both call it).
        ///
        /// Defensive: when an existing instance is returned, this also re-enables the
        /// always-on maps (UI + Editors). They can drift to disabled if a previous
        /// code path (a test's <see cref="ResetForTests"/>, a hot-reload, an editor's
        /// teardown that touched the canonical asset) left them off — and since with
        /// Domain Reload off the canonical <see cref="InputActionAsset"/> persists
        /// across sessions, the disabled state would otherwise be sticky and break
        /// the entire input pipeline (mouse clicks AND F-keys).
        /// </summary>
        public static InputService Initialize()
        {
            if (_instance != null)
            {
                EnsureAlwaysOnMapsEnabled(_instance);
                return _instance;
            }

            var asset = Resources.Load<InputActionAsset>(CanonicalAssetResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[InputService] Canonical asset missing at " +
                    $"Resources/{CanonicalAssetResourcePath}. Input pipeline cannot bootstrap.");
                return null;
            }

            // Use the canonical asset directly — Object.Instantiate produces a
            // ScriptableObject clone whose actions register with the Input System's
            // runtime tracking but whose <c>WasPerformedThisFrame</c> /
            // <c>ReadValue</c> calls observed unreliable wiring on Unity 2022.3.
            // Since Valkur has a single local player (no split-screen), all
            // consumers share the same canonical asset safely.
            _instance = new InputService(asset);
            ServiceLocator.Register(_instance);
            return _instance;
        }

        /// <summary>
        /// Re-enables UI + Editors maps on an already-built service. Public so the
        /// watchdog and tests can force a self-heal pass without going through the
        /// full bootstrap chain.
        /// </summary>
        public static void EnsureAlwaysOnMapsEnabled(InputService svc)
        {
            if (svc == null) return;
            if (svc.UI != null      && svc.UI.Map != null      && !svc.UI.Map.enabled)      svc.UI.Map.Enable();
            if (svc.Editors != null && svc.Editors.Map != null && !svc.Editors.Map.enabled) svc.Editors.Map.Enable();
        }

        /// <summary>Test hook: drop the service so a fresh one can be initialized.
        /// Disables the maps so the next consumer starts from a clean slate, but does
        /// NOT destroy the canonical asset (it lives in Resources, shared across the
        /// session and survives Domain Reload off / Play mode toggles).
        /// Public so EditMode / PlayMode tests in a sibling assembly can call it.</summary>
        public static void ResetForTests()
        {
            if (_instance != null)
            {
                _instance.UI.Map.Disable();
                _instance.Editors.Map.Disable();
                _instance.Gameplay.Map.Disable();
            }
            _instance = null;
            ServiceLocator.Unregister<InputService>();
        }

        // ─── Typed map accessors ────────────────────────────────────────────────

        public sealed class UIActions
        {
            public InputActionMap Map { get; }
            public InputAction Point { get; }
            public InputAction Click { get; }
            public InputAction RightClick { get; }
            public InputAction MiddleClick { get; }
            public InputAction ScrollWheel { get; }
            public InputAction Navigate { get; }
            public InputAction Submit { get; }
            public InputAction Cancel { get; }

            internal UIActions(InputActionMap map)
            {
                Map         = map;
                Point       = map.FindAction("Point",       throwIfNotFound: true);
                Click       = map.FindAction("Click",       throwIfNotFound: true);
                RightClick  = map.FindAction("RightClick",  throwIfNotFound: true);
                MiddleClick = map.FindAction("MiddleClick", throwIfNotFound: true);
                ScrollWheel = map.FindAction("ScrollWheel", throwIfNotFound: true);
                Navigate    = map.FindAction("Navigate",    throwIfNotFound: true);
                Submit      = map.FindAction("Submit",      throwIfNotFound: true);
                Cancel      = map.FindAction("Cancel",      throwIfNotFound: true);
            }
        }

        public sealed class GameplayActions
        {
            public InputActionMap Map { get; }
            public InputAction Move { get; }
            public InputAction Look { get; }
            public InputAction PrimaryAttack { get; }
            public InputAction SecondaryAttack { get; }
            public InputAction Dash { get; }
            public InputAction Interact { get; }
            public InputAction Inventory { get; }
            public InputAction Spell1 { get; }
            public InputAction Spell2 { get; }
            public InputAction Spell3 { get; }
            public InputAction Spell4 { get; }
            public InputAction Pause { get; }

            internal GameplayActions(InputActionMap map)
            {
                Map             = map;
                Move            = map.FindAction("Move",            throwIfNotFound: true);
                Look            = map.FindAction("Look",            throwIfNotFound: true);
                PrimaryAttack   = map.FindAction("PrimaryAttack",   throwIfNotFound: true);
                SecondaryAttack = map.FindAction("SecondaryAttack", throwIfNotFound: true);
                Dash            = map.FindAction("Dash",            throwIfNotFound: true);
                Interact        = map.FindAction("Interact",        throwIfNotFound: true);
                Inventory       = map.FindAction("Inventory",       throwIfNotFound: true);
                Spell1          = map.FindAction("Spell1",          throwIfNotFound: true);
                Spell2          = map.FindAction("Spell2",          throwIfNotFound: true);
                Spell3          = map.FindAction("Spell3",          throwIfNotFound: true);
                Spell4          = map.FindAction("Spell4",          throwIfNotFound: true);
                Pause           = map.FindAction("Pause",           throwIfNotFound: true);
            }
        }

        public sealed class EditorsActions
        {
            public InputActionMap Map { get; }
            public InputAction ToggleParticles { get; }
            public InputAction ToggleCombatRanges { get; }
            public InputAction ToggleSpawner { get; }
            public InputAction ToggleLighting { get; }
            public InputAction ToggleSpells { get; }
            public InputAction ToggleEntities { get; }
            public InputAction ToggleInventory { get; }
            public InputAction ToggleItems { get; }
            public InputAction ToggleTile { get; }
            public InputAction ToggleDebugHUD { get; }
            public InputAction ToggleBuildings { get; }
            public InputAction ToggleMap { get; }
            public InputAction ToggleFSM { get; }
            public InputAction QuickSave { get; }
            public InputAction QuickLoad { get; }
            public InputAction CtrlModifier { get; }
            public InputAction AltModifier { get; }
            public InputAction ToggleDevConsole { get; }

            internal EditorsActions(InputActionMap map)
            {
                Map                = map;
                ToggleParticles    = map.FindAction("ToggleParticles",    throwIfNotFound: true);
                ToggleCombatRanges = map.FindAction("ToggleCombatRanges", throwIfNotFound: true);
                ToggleSpawner      = map.FindAction("ToggleSpawner",      throwIfNotFound: true);
                ToggleLighting     = map.FindAction("ToggleLighting",     throwIfNotFound: true);
                ToggleSpells       = map.FindAction("ToggleSpells",       throwIfNotFound: true);
                ToggleEntities     = map.FindAction("ToggleEntities",     throwIfNotFound: true);
                ToggleInventory    = map.FindAction("ToggleInventory",    throwIfNotFound: true);
                ToggleItems        = map.FindAction("ToggleItems",        throwIfNotFound: true);
                ToggleTile         = map.FindAction("ToggleTile",         throwIfNotFound: true);
                ToggleDebugHUD     = map.FindAction("ToggleDebugHUD",     throwIfNotFound: true);
                ToggleBuildings    = map.FindAction("ToggleBuildings",    throwIfNotFound: true);
                ToggleMap          = map.FindAction("ToggleMap",          throwIfNotFound: true);
                ToggleFSM          = map.FindAction("ToggleFSM",          throwIfNotFound: true);
                QuickSave          = map.FindAction("QuickSave",          throwIfNotFound: true);
                QuickLoad          = map.FindAction("QuickLoad",          throwIfNotFound: true);
                CtrlModifier       = map.FindAction("CtrlModifier",       throwIfNotFound: true);
                AltModifier        = map.FindAction("AltModifier",        throwIfNotFound: true);
                ToggleDevConsole   = map.FindAction("ToggleDevConsole",   throwIfNotFound: true);
            }
        }
    }
}
