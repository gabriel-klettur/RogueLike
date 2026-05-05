namespace Valkur.UI
{
    /// <summary>
    /// Descriptor for a single in-game editor shown in the Controls Settings
    /// "Editors" sub-tab strip.
    ///
    /// <para>
    /// The <c>HasUndo</c>, <c>HasRedo</c>, <c>HasSave</c> and <c>HasEsc</c> flags
    /// are sourced from each editor's actual <c>KeyboardInputManager</c> calls (grep
    /// for <c>IsCtrlHeld + WasKeyPressedThisFrame(Key.Z/Y/S)</c> and
    /// <c>WasEscapePressedThisFrame</c>). They are intentionally read-only in the UI
    /// because making those shortcuts rebindable would require migrating every editor
    /// to InputAction-backed lookups — deferred to a future pass.
    /// </para>
    /// </summary>
    internal sealed class EditorSubTabData
    {
        /// <summary>Short label shown on the sub-tab button (fits ~120 px).</summary>
        public string ShortLabel { get; }

        /// <summary>Full label used as row caption inside the sub-tab content area.</summary>
        public string ToggleLabel { get; }

        /// <summary>
        /// <c>GameSettingsBindings</c> action name used to read/write the toggle key.
        /// </summary>
        public string ActionName { get; }

        /// <summary>
        /// True when the binding is fixed (e.g. Lighting = Ctrl+F3).
        /// A read-only cell is rendered instead of a rebind button.
        /// </summary>
        public bool IsFixedBinding { get; }

        public bool HasUndo { get; }
        public bool HasSave { get; }
        public bool HasEsc  { get; }

        public EditorSubTabData(string shortLabel, string toggleLabel,
            string actionName, bool isFixedBinding,
            bool hasUndo, bool hasSave, bool hasEsc)
        {
            ShortLabel      = shortLabel;
            ToggleLabel     = toggleLabel;
            ActionName      = actionName;
            IsFixedBinding  = isFixedBinding;
            HasUndo         = hasUndo;
            HasSave         = hasSave;
            HasEsc          = hasEsc;
        }

        // ── Canonical list (F-key order, matching EditorHotkeyBindings.Hotkey) ──
        //
        // Shortcut matrix verified by grepping editor partials for:
        //   IsCtrlHeld + WasKeyPressedThisFrame(Key.Z)  → Undo
        //   IsCtrlHeld + WasKeyPressedThisFrame(Key.Y)  → Redo (implied when Undo present)
        //   IsCtrlHeld + WasKeyPressedThisFrame(Key.S)  → Save
        //   WasEscapePressedThisFrame                   → Esc close
        //   For Map and FSM: close is via toggle key (F11/F12), not Esc.
        //
        // Row A: Particles | Time & Weather | Spawners | Lighting | Spells | Entities
        // Row B: Inventory | Items          | Tile     | Buildings| Map    | FSM

        public static readonly EditorSubTabData[] All = new[]
        {
            // ── Row A ──────────────────────────────────────────────────────────
            new EditorSubTabData(
                "Particles F1",
                "Toggle Particles Editor",
                "toggle_particles_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Time & Weather F2",
                "Toggle Time & Weather Editor",
                "toggle_time_weather_editor",
                isFixedBinding: false,
                hasUndo: false, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Spawners F3",
                "Toggle Spawners Editor",
                "toggle_spawner_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            // Lighting uses a hardcoded Ctrl+F3 modifier — toggle key is fixed.
            new EditorSubTabData(
                "Lighting Ctrl+F3",
                "Toggle Lighting Editor",
                "toggle_lighting_editor",
                isFixedBinding: true,
                hasUndo: true, hasSave: true, hasEsc: true),

            new EditorSubTabData(
                "Spells F4",
                "Toggle Spells Editor",
                "toggle_spells_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Entities F5",
                "Toggle Entities Editor",
                "toggle_entities_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            // ── Row B ──────────────────────────────────────────────────────────
            new EditorSubTabData(
                "Inventory F6",
                "Toggle Inventory Editor",
                "toggle_inventory_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Items F7",
                "Toggle Items Editor",
                "toggle_items_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Tile F8",
                "Toggle Tile Editor",
                "toggle_tile_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: true),

            new EditorSubTabData(
                "Buildings F10",
                "Toggle Buildings Editor",
                "toggle_buildings_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: true, hasEsc: true),

            // Map Editor uses F11 to close — no Esc, no Undo, no Save shortcut.
            new EditorSubTabData(
                "Map F11",
                "Toggle Map Editor",
                "toggle_map_editor",
                isFixedBinding: false,
                hasUndo: false, hasSave: false, hasEsc: false),

            // FSM Editor uses F12 to close — no Esc, but has Undo/Redo buttons.
            new EditorSubTabData(
                "FSM F12",
                "Toggle FSM Editor",
                "toggle_fsm_editor",
                isFixedBinding: false,
                hasUndo: true, hasSave: false, hasEsc: false),
        };
    }
}
