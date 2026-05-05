using System;
using System.Collections.Generic;
using System.Reflection;

namespace Valkur.Core
{
    /// <summary>
    /// Indirection layer that lets UI code read/write <see cref="GameSettings"/> input
    /// binding fields by logical action name (without a large switch).
    ///
    /// Mirrors Python's input_service: action names map to a list of binding strings.
    /// Unity stores up to two keyboard bindings per action (*KeyA, *KeyB) and optionally
    /// a mouse binding for attack-like actions.
    /// </summary>
    public static class GameSettingsBindings
    {
        /// <summary>
        /// Action -> list of (fieldName, label) entries on GameSettings.
        /// </summary>
        private static readonly Dictionary<string, List<(string field, string label)>> _table
            = new Dictionary<string, List<(string, string)>>
        {
            { "pause",               new List<(string,string)> { ("pauseKeyA", "primary") } },
            { "toggle_inventory",    new List<(string,string)> { ("toggleInventoryKeyA", "primary") } },
            { "move_up",             new List<(string,string)> { ("moveUpKeyA","primary"), ("moveUpKeyB","secondary") } },
            { "move_down",           new List<(string,string)> { ("moveDownKeyA","primary"), ("moveDownKeyB","secondary") } },
            { "move_left",           new List<(string,string)> { ("moveLeftKeyA","primary"), ("moveLeftKeyB","secondary") } },
            { "move_right",          new List<(string,string)> { ("moveRightKeyA","primary"), ("moveRightKeyB","secondary") } },
            { "dash",                new List<(string,string)> { ("dashKeyA","primary"), ("dashKeyB","secondary") } },
            { "spell_1",             new List<(string,string)> { ("spell1KeyA","primary") } },
            { "spell_2",             new List<(string,string)> { ("spell2KeyA","primary") } },
            { "spell_3",             new List<(string,string)> { ("spell3KeyA","primary") } },
            { "spell_4",             new List<(string,string)> { ("spell4KeyA","primary") } },
            { "attack_primary_mouse",   new List<(string,string)> { ("primaryAttackMouse","primary") } },
            { "attack_secondary_mouse", new List<(string,string)> { ("secondaryAttackMouse","primary") } },
            { "toggle_tile_editor",         new List<(string,string)> { ("toggleTileEditorKeyA","primary") } },
            { "toggle_map_editor",          new List<(string,string)> { ("toggleMapEditorKeyA","primary") } },
            { "toggle_particles_editor",    new List<(string,string)> { ("toggleParticlesEditorKeyA","primary") } },
            { "toggle_time_weather_editor", new List<(string,string)> { ("toggleTimeWeatherEditorKeyA","primary") } },
            { "toggle_spawner_editor",      new List<(string,string)> { ("toggleSpawnerEditorKeyA","primary") } },
            { "toggle_lighting_editor",     new List<(string,string)> { ("toggleLightingEditorKeyA","primary") } },
            { "toggle_spells_editor",       new List<(string,string)> { ("toggleSpellsEditorKeyA","primary") } },
            { "toggle_entities_editor",     new List<(string,string)> { ("toggleEntitiesEditorKeyA","primary") } },
            { "toggle_inventory_editor",    new List<(string,string)> { ("toggleInventoryEditorKeyA","primary") } },
            { "toggle_items_editor",        new List<(string,string)> { ("toggleItemsEditorKeyA","primary") } },
            { "toggle_buildings_editor",    new List<(string,string)> { ("toggleBuildingsEditorKeyA","primary") } },
            { "toggle_fsm_editor",          new List<(string,string)> { ("toggleFsmEditorKeyA","primary") } },
        };

        public static IReadOnlyList<string> AllActions
        {
            get
            {
                var list = new List<string>(_table.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        /// <summary>Returns the number of bindings for the given action (1 or 2).</summary>
        public static int GetBindingCount(string action)
            => _table.TryGetValue(action, out var list) ? list.Count : 0;

        /// <summary>Returns the current value for (action, index). Returns empty if unknown.</summary>
        public static string Get(GameSettings gs, string action, int index)
        {
            if (gs == null || !_table.TryGetValue(action, out var list)) return "";
            if (index < 0 || index >= list.Count) return "";
            var fi = typeof(GameSettings).GetField(list[index].field, BindingFlags.Instance | BindingFlags.Public);
            return fi?.GetValue(gs) as string ?? "";
        }

        /// <summary>Writes a new binding. Returns true on success.</summary>
        public static bool Set(GameSettings gs, string action, int index, string value)
        {
            if (gs == null || !_table.TryGetValue(action, out var list)) return false;
            if (index < 0 || index >= list.Count) return false;
            var fi = typeof(GameSettings).GetField(list[index].field, BindingFlags.Instance | BindingFlags.Public);
            if (fi == null) return false;
            fi.SetValue(gs, value ?? "");
            return true;
        }

        public static string Label(string action, int index)
        {
            if (!_table.TryGetValue(action, out var list)) return "";
            if (index < 0 || index >= list.Count) return "";
            return list[index].label;
        }
    }
}
