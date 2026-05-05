using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Bridges the user's saved <see cref="GameSettings"/> editor-toggle key choices
    /// into the live <see cref="InputService.EditorsActions"/> bindings at runtime.
    ///
    /// Without this applier the Controls Settings rebind panel is cosmetic — the player
    /// can change the stored value but the actual InputAction still fires on the original
    /// default F-key. This class applies the stored override on every Play Mode entry and
    /// after each save so the runtime and the settings file stay in sync.
    ///
    /// NOTE: The Lighting editor Ctrl+F3 modifier is hardcoded inside
    /// <see cref="Valkur.Gameplay.Editors.LightingRuntimeEditor"/> and is NOT managed
    /// here. Only the bare key portion is overridden for ToggleLighting — the caller
    /// must still hold Ctrl for the editor to respond. A future pass could migrate
    /// that check to a composite InputAction once all editor shortcuts use InputActions.
    /// </summary>
    public static class EditorBindingsApplier
    {
        /// <summary>
        /// Required for Domain Reload OFF: resets any static state so each Play Mode
        /// entry starts from a clean slate (currently a no-op but kept as the hook so
        /// future state fields are automatically handled).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter() { /* no-op: stateless class */ }

        /// <summary>
        /// Applies every editor toggle override from <see cref="GameSettings.Instance"/>
        /// onto the canonical <see cref="InputService"/> actions. Safe to call multiple
        /// times (idempotent per session — always re-reads the latest GameSettings values).
        /// Called automatically on Play Mode entry (via <see cref="RuntimeInputBootstrap"/>)
        /// and immediately after any rebind save.
        /// </summary>
        public static void ReapplyAll()
        {
            var svc = InputService.Instance;
            if (svc == null)
            {
                Debug.LogWarning("[EditorBindingsApplier] InputService not ready — skipping.");
                return;
            }

            var gs = GameSettings.Instance;
            if (gs == null) return;

            var e = svc.Editors;
            Apply(e.ToggleParticles,   gs.toggleParticlesEditorKeyA);
            Apply(e.ToggleTimeWeather, gs.toggleTimeWeatherEditorKeyA);
            Apply(e.ToggleSpawner,     gs.toggleSpawnerEditorKeyA);
            Apply(e.ToggleLighting,    gs.toggleLightingEditorKeyA);
            Apply(e.ToggleSpells,      gs.toggleSpellsEditorKeyA);
            Apply(e.ToggleEntities,    gs.toggleEntitiesEditorKeyA);
            Apply(e.ToggleInventory,   gs.toggleInventoryEditorKeyA);
            Apply(e.ToggleItems,       gs.toggleItemsEditorKeyA);
            Apply(e.ToggleTile,        gs.toggleTileEditorKeyA);
            Apply(e.ToggleBuildings,   gs.toggleBuildingsEditorKeyA);
            Apply(e.ToggleMap,         gs.toggleMapEditorKeyA);
            Apply(e.ToggleFSM,         gs.toggleFsmEditorKeyA);

            Debug.Log("[EditorBindingsApplier] Editor toggle bindings applied from GameSettings.");
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Converts a GameSettings key string ("F8", "Escape", "Space", etc.) to the
        /// Unity Input System path component used under &lt;Keyboard&gt;/.
        /// Unity's canonical control names are all lowercase (e.g. "f8", "escape", "space").
        /// Named function keys follow the "fN" pattern; letter keys are already single chars.
        /// </summary>
        private static string ConvertKeyName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            // Normalise: trim and lower-case, then patch the few known mismatches between
            // GameSettings string conventions and Unity Input System's <Keyboard>/... naming.
            // Unity Input System uses camelCase for multi-word control names (e.g. "upArrow",
            // "leftCtrl") even though it does case-insensitive lookup. We produce the canonical
            // form so ApplyBindingOverride paths are unambiguous.
            var lower = key.Trim().ToLowerInvariant();
            return lower switch
            {
                "uparrow"    => "upArrow",
                "downarrow"  => "downArrow",
                "leftarrow"  => "leftArrow",
                "rightarrow" => "rightArrow",
                "rightctrl"  => "rightCtrl",
                "leftctrl"   => "leftCtrl",
                "rightshift" => "rightShift",
                "leftshift"  => "leftShift",
                "leftbutton" => null,         // mouse — not applicable here
                "rightbutton"=> null,
                _            => lower         // "f1"…"f12", single letters, "escape", "space", etc.
            };
        }

        private static void Apply(InputAction action, string keyName)
        {
            if (action == null) return;
            var path = ConvertKeyName(keyName);
            if (string.IsNullOrEmpty(path)) return;

            var fullPath = $"<Keyboard>/{path}";
            // Only slot 0 (primary binding) is managed here. Composite/secondary slots
            // are intentionally left untouched to avoid clobbering modifier chains.
            try
            {
                action.ApplyBindingOverride(0, fullPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[EditorBindingsApplier] Could not apply override " +
                    $"'{fullPath}' to '{action.name}': {ex.Message}");
            }
        }
    }
}
