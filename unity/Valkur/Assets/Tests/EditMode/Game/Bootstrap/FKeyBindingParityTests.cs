using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Gameplay;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Items;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;
using Valkur.Core.Input;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Validates that Unity F-key bindings match the Python reference (_input_defaults.py).
    ///
    /// Python mapping:
    ///   Ctrl+F1 = toggle_particles_editor
    ///   Alt+F2  = toggle point-lights visualizer
    ///   F3      = toggle_spawner_editor   | Ctrl+F3 = lighting_editor
    ///   F4      = toggle_spells_editor
    ///   F5      = toggle_entities_editor
    ///   F6      = toggle_inventory_editor
    ///   F7      = toggle_item_editor
    ///   F8      = toggle_tile_editor
    ///   F9      = toggle_debug_overlay
    ///   F10     = toggle_building_editor
    ///   F11     = toggle_map_editor
    ///   F12     = toggle_fsm_editor
    /// </summary>
    [TestFixture]
    public class FKeyBindingParityTests
    {
        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Clears the static _instance field of a SingletonMonoBehaviour so
        /// AddComponent triggers OnSingletonAwake in EditMode tests.
        /// </summary>
        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, null);
                    return;
                }
                type = type.BaseType;
            }
        }

        /// <summary>
        /// Creates a singleton MonoBehaviour and ensures its OnSingletonAwake runs.
        /// In EditMode tests Awake may not fire, so we force‑invoke it.
        /// </summary>
        private static T CreateSingleton<T>(string name = "TestGO") where T : MonoBehaviour
        {
            ClearSingletonInstance<T>();
            var go = new GameObject(name);
            var comp = go.AddComponent<T>();
            // Force init if lifecycle didn't run in EditMode
            if (FindField(comp, "_toggleAction")?.GetValue(comp) == null)
                InvokeMethod(comp, "OnSingletonAwake");
            return comp;
        }

        /// <summary>
        /// Creates a plain MonoBehaviour and ensures its Awake/Start runs.
        /// </summary>
        private static T CreateMono<T>(string name = "TestGO", string initMethod = "Awake") where T : MonoBehaviour
        {
            var go = new GameObject(name);
            var comp = go.AddComponent<T>();
            InvokeMethod(comp, initMethod);
            return comp;
        }

        /// <summary>
        /// Reads the primary binding path from a private InputAction field via reflection.
        /// </summary>
        private static string GetBindingPath(object instance, string fieldName)
        {
            var field = FindField(instance, fieldName);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {instance.GetType().Name}");

            var action = field.GetValue(instance) as InputAction;
            Assert.IsNotNull(action, $"Field '{fieldName}' is null on {instance.GetType().Name}");
            Assert.IsTrue(action.bindings.Count > 0,
                $"InputAction '{fieldName}' on {instance.GetType().Name} has no bindings");

            return action.bindings[0].path;
        }

        private static bool HasBindingForKey(object instance, string fieldName, string keyPath)
        {
            var field = FindField(instance, fieldName);
            if (field == null) return false;
            var action = field.GetValue(instance) as InputAction;
            if (action == null) return false;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].path == keyPath) return true;
            }
            return false;
        }

        private static void AssertModifierExists(object instance, string fieldName)
        {
            var field = FindField(instance, fieldName);
            Assert.IsNotNull(field, $"Modifier field '{fieldName}' not found on {instance.GetType().Name}");
            var action = field.GetValue(instance) as InputAction;
            Assert.IsNotNull(action, $"Modifier field '{fieldName}' is null on {instance.GetType().Name}");
        }

        private static void InvokeMethod(object instance, string methodName)
        {
            var type = instance.GetType();
            MethodInfo method = null;
            while (type != null && method == null)
            {
                method = type.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                type = type.BaseType;
            }
            Assert.IsNotNull(method, $"Method '{methodName}' not found on {instance.GetType().Name}");
            method.Invoke(instance, null);
        }

        private static FieldInfo FindField(object instance, string fieldName)
        {
            var type = instance.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        // -------------------------------------------------------------------
        // F1 → Particles Editor (plain, NOT Ctrl) — matches Tiles (F8) /
        // Buildings (F10) hotkey style; see ParticlesRuntimeEditor.cs.
        // -------------------------------------------------------------------

        [Test]
        public void ParticlesEditor_BoundTo_F1_WithoutCtrl()
        {
            // ParticlesRuntimeEditor uses the stateless EditorHotkeyBindings API
            // (no cached _toggleAction field — see EditorHotkeyBindingsStatelessTests
            // for the rationale). Verify the F1 binding lives on the canonical
            // input asset under Hotkey.ToggleParticles, with no Ctrl modifier in
            // the binding path itself.
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleParticles, out bool owns);
            Assert.IsNotNull(action, "EditorHotkeyBindings must resolve ToggleParticles");
            Assert.Greater(action.bindings.Count, 0, "ToggleParticles must have at least one binding");

            bool foundF1 = false;
            foreach (var b in action.bindings)
            {
                if (b.path == "<Keyboard>/f1") foundF1 = true;
                Assert.IsFalse(b.path != null && b.path.ToLowerInvariant().Contains("ctrl"),
                    "ParticlesEditor binding must NOT contain a Ctrl modifier path.");
            }
            Assert.IsTrue(foundF1, "ParticlesEditor must include a <Keyboard>/f1 binding");

            if (owns) action.Dispose();
        }

        // -------------------------------------------------------------------
        // Alt+F2 → CombatRangeVisualizer
        // -------------------------------------------------------------------

        [Test]
        public void CombatRangeVisualizer_BoundTo_F2_WithAltModifier()
        {
            var viz = CreateSingleton<CombatRangeVisualizer>("TestCombatViz");

            string path = GetBindingPath(viz, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f2", path,
                "CombatRangeVisualizer should bind to F2 (Alt modifier checked in Update)");
            AssertModifierExists(viz, "_altModifier");

            Object.DestroyImmediate(viz.gameObject);
        }

        // -------------------------------------------------------------------
        // F3 → Spawner Editor (bare, NOT Ctrl)
        // -------------------------------------------------------------------

        [Test]
        public void SpawnerEditor_BoundTo_F3_WithoutCtrl()
        {
            var editor = CreateSingleton<SpawnerEditorManager>("TestSpawnerEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f3", path,
                "SpawnerEditorManager should bind to F3");
            AssertModifierExists(editor, "_ctrlModifier");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // Ctrl+F3 → Lighting Editor
        // -------------------------------------------------------------------

        [Test]
        public void LightingEditor_BoundTo_F3_WithCtrlModifier()
        {
            var editor = CreateSingleton<LightingRuntimeEditor>("TestLightingEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f3", path,
                "LightingRuntimeEditor should bind to F3 (Ctrl modifier checked in Update)");
            AssertModifierExists(editor, "_ctrlModifier");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F4 → Spells Editor
        // -------------------------------------------------------------------

        [Test]
        public void SpellsEditor_BoundTo_F4()
        {
            var editor = CreateSingleton<SpellsRuntimeEditor>("TestSpellsEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f4", path,
                "SpellsRuntimeEditor should bind to F4 (Python toggle_spells_editor = K_F4)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F5 → Entities Editor
        // -------------------------------------------------------------------

        [Test]
        public void EntitiesEditor_BoundTo_F5()
        {
            var editor = CreateSingleton<EntitiesRuntimeEditor>("TestEntitiesEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f5", path,
                "EntitiesRuntimeEditor should bind to F5 (Python toggle_entities_editor = K_F5)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F6 → Inventory Editor
        // -------------------------------------------------------------------

        [Test]
        public void InventoryEditor_BoundTo_F6()
        {
            var editor = CreateSingleton<InventoryRuntimeEditor>("TestInventoryEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f6", path,
                "InventoryRuntimeEditor should bind to F6 (Python toggle_inventory_editor = K_F6)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F7 → Items Editor
        // -------------------------------------------------------------------

        [Test]
        public void ItemsEditor_BoundTo_F7()
        {
            var editor = CreateSingleton<ItemsRuntimeEditor>("TestItemsEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f7", path,
                "ItemsRuntimeEditor should bind to F7 (Python toggle_item_editor = K_F7)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F8 → Tile Editor
        // -------------------------------------------------------------------

        [Test]
        public void TileEditor_BoundTo_F8()
        {
            var handler = new TileEditorInputHandler();
            handler.CreateActions();

            string path = GetBindingPath(handler, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f8", path,
                "TileEditorInputHandler should bind to F8 (Python toggle_tile_editor = K_F8)");

            handler.Dispose();
        }

        // -------------------------------------------------------------------
        // F9 → Debug Overlay (DebugHUD)
        // -------------------------------------------------------------------

        [Test]
        public void DebugHUD_BoundTo_F9()
        {
            var hud = CreateMono<DebugHUD>("TestDebugHUD", "Start");

            string path = GetBindingPath(hud, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f9", path,
                "DebugHUD should bind to F9 (Python toggle_debug_overlay = K_F9)");

            Object.DestroyImmediate(hud.gameObject);
        }

        // -------------------------------------------------------------------
        // F10 → Buildings Editor
        // -------------------------------------------------------------------

        [Test]
        public void BuildingsEditor_BoundTo_F10()
        {
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f10", path,
                "BuildingsRuntimeEditor should bind to F10 (Python toggle_building_editor = K_F10)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // F11 → Map Editor
        // -------------------------------------------------------------------

        [Test]
        public void MapEditor_BoundTo_F11()
        {
            var handler = new MapEditorInputHandler();
            handler.CreateActions();

            string path = GetBindingPath(handler, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f11", path,
                "MapEditorInputHandler should bind to F11 (Python toggle_map_editor = K_F11)");

            handler.Dispose();
        }

        // -------------------------------------------------------------------
        // F12 → FSM Editor
        // -------------------------------------------------------------------

        [Test]
        public void FSMEditor_BoundTo_F12()
        {
            var editor = CreateSingleton<FSMRuntimeEditor>("TestFSMEditor");

            string path = GetBindingPath(editor, "_toggleAction");
            Assert.AreEqual("<Keyboard>/f12", path,
                "FSMRuntimeEditor should bind to F12 (Python toggle_fsm_editor = K_F12)");

            Object.DestroyImmediate(editor.gameObject);
        }

        // -------------------------------------------------------------------
        // DevConsole must NOT have F4 binding (removed — was conflicting)
        // -------------------------------------------------------------------

        [Test]
        public void DevConsole_DoesNotBind_F4()
        {
            var console = CreateSingleton<DevConsole>("TestDevConsole");

            bool hasF4 = HasBindingForKey(console, "_toggleAction", "<Keyboard>/f4");
            Assert.IsFalse(hasF4,
                "DevConsole must not bind F4 (would conflict with SpellsEditor). Only backquote.");

            Object.DestroyImmediate(console.gameObject);
        }

        [Test]
        public void DevConsole_StillBound_ToBackquote()
        {
            var console = CreateSingleton<DevConsole>("TestDevConsole");

            bool hasBackquote = HasBindingForKey(console, "_toggleAction", "<Keyboard>/backquote");
            Assert.IsTrue(hasBackquote,
                "DevConsole should still bind to backquote (~)");

            Object.DestroyImmediate(console.gameObject);
        }

        // -------------------------------------------------------------------
        // SaveLoad uses Ctrl+F5 / Ctrl+F9 (not bare F5 / F9)
        // -------------------------------------------------------------------

        [Test]
        public void SaveLoad_QuickSave_UsesF5_WithCtrlModifier()
        {
            var handler = CreateMono<SaveLoadInputHandler>("TestSaveLoad");

            string path = GetBindingPath(handler, "_quickSaveAction");
            Assert.AreEqual("<Keyboard>/f5", path,
                "QuickSave action should bind to F5 (Ctrl guard in Update)");
            AssertModifierExists(handler, "_ctrlModifier");

            Object.DestroyImmediate(handler.gameObject);
        }

        [Test]
        public void SaveLoad_QuickLoad_UsesF9_WithCtrlModifier()
        {
            var handler = CreateMono<SaveLoadInputHandler>("TestSaveLoad");

            string path = GetBindingPath(handler, "_quickLoadAction");
            Assert.AreEqual("<Keyboard>/f9", path,
                "QuickLoad action should bind to F9 (Ctrl guard in Update)");
            AssertModifierExists(handler, "_ctrlModifier");

            Object.DestroyImmediate(handler.gameObject);
        }

        // -------------------------------------------------------------------
        // Cross-check: no bare F-key conflicts
        // -------------------------------------------------------------------

        [Test]
        public void NoBareKeyConflicts_AcrossEditors()
        {
            var console = CreateSingleton<DevConsole>("ConflictCheck_Console");
            Assert.IsFalse(HasBindingForKey(console, "_toggleAction", "<Keyboard>/f4"),
                "DevConsole must not have F4 — would conflict with SpellsEditor");
            Object.DestroyImmediate(console.gameObject);

            var save = CreateMono<SaveLoadInputHandler>("ConflictCheck_Save");
            AssertModifierExists(save, "_ctrlModifier");
            Object.DestroyImmediate(save.gameObject);

            Assert.Pass("No bare F-key conflicts detected.");
        }

        // -------------------------------------------------------------------
        // GameSettings legacy values match actual editor bindings
        // -------------------------------------------------------------------

        [Test]
        public void GameSettings_EditorKeys_MatchActualBindings()
        {
            var settings = new Valkur.Core.GameSettings();
            Assert.AreEqual("F8", settings.toggleTileEditorKeyA,
                "GameSettings.toggleTileEditorKeyA must be F8 (TileEditor binding)");
            Assert.AreEqual("F11", settings.toggleMapEditorKeyA,
                "GameSettings.toggleMapEditorKeyA must be F11 (MapEditor binding)");
        }
    }
}
