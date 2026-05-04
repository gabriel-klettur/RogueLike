using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Services;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Items;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Builds the static catalogue of buttons rendered in the General Editor
    /// (ESC) launcher. Three sections:
    /// <list type="bullet">
    /// <item><b>Editores</b> – the eleven F-key editors. Each entry calls
    /// <see cref="GameEditorManager.OpenExclusive"/>, which auto-closes the
    /// launcher (since the launcher is itself the active editor).</item>
    /// <item><b>Diagnóstico</b> – overlay toggles that don't participate in
    /// the editor exclusivity contract; the launcher stays open.</item>
    /// <item><b>Partida</b> – session actions (save / load / options / quit)
    /// that close the launcher first and then route through the pause menu
    /// service or scene transition.</item>
    /// </list>
    /// </summary>
    public static class GeneralEditorRegistry
    {
        public static IReadOnlyList<GeneralEditorEntry> BuildEntries()
        {
            var list = new List<GeneralEditorEntry>(17);

            // ── Editores ────────────────────────────────────────────────────
            list.Add(MakeEditor("Tile",      () => TileEditorManager.Instance));
            list.Add(MakeEditor("Edificios", () => BuildingsRuntimeEditor.Instance));
            list.Add(MakeEditor("Items",     () => ItemsRuntimeEditor.Instance));
            list.Add(MakeEditor("Hechizos",  () => SpellsRuntimeEditor.Instance));
            list.Add(MakeEditor("Entidades", () => EntitiesRuntimeEditor.Instance));
            list.Add(MakeEditor("FSM",       () => FSMRuntimeEditor.Instance));
            list.Add(MakeEditor("Mapa",      () => MapEditorManager.Instance));
            list.Add(MakeEditor("Inventario",() => InventoryRuntimeEditor.Instance));
            list.Add(MakeEditor("Partículas",() => ParticlesRuntimeEditor.Instance));
            list.Add(MakeEditor("Spawners",  () => SpawnerEditorManager.Instance));
            list.Add(MakeEditor("Iluminación", () => LightingRuntimeEditor.Instance));

            // ── Diagnóstico (toggles, no exclusive activation) ──────────────
            list.Add(new GeneralEditorEntry(
                "Rangos combate", GeneralEditorSection.Diagnostics,
                onClick:  () => CombatRangeVisualizer.Instance?.ToggleVisible(),
                isActive: () => CombatRangeVisualizer.Instance != null
                                && CombatRangeVisualizer.Instance.IsVisible));

            list.Add(new GeneralEditorEntry(
                "HUD debug", GeneralEditorSection.Diagnostics,
                onClick:  () => ServiceLocator.Get<IDebugOverlayService>()?.ToggleVisible(),
                isActive: () => ServiceLocator.Get<IDebugOverlayService>()?.IsVisible == true));

            // ── Partida ─────────────────────────────────────────────────────
            list.Add(new GeneralEditorEntry(
                "Guardar", GeneralEditorSection.Game,
                onClick: TryQuickSave,
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Cargar", GeneralEditorSection.Game,
                onClick: () => ServiceLocator.Get<IPauseMenuService>()?.OpenLoadGame(),
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Opciones", GeneralEditorSection.Game,
                onClick: () => ServiceLocator.Get<IPauseMenuService>()?.OpenOptions(),
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Salir al menú", GeneralEditorSection.Game,
                onClick: QuitToMainMenu,
                closesLauncher: true));

            return list;
        }

        // Helper for the eleven IGameEditor entries — each looks up the live
        // singleton at click time and routes through GameEditorManager.
        private static GeneralEditorEntry MakeEditor<T>(string label, Func<T> getter)
            where T : Component, GameEditorManager.IGameEditor
        {
            return new GeneralEditorEntry(
                label, GeneralEditorSection.Editors,
                onClick: () =>
                {
                    var ed = getter();
                    if (ed == null)
                    {
                        Debug.LogWarning($"[GeneralEditor] '{label}' instance not available.");
                        return;
                    }
                    var mgr = GameEditorManager.Instance;
                    if (mgr == null)
                    {
                        Debug.LogWarning("[GeneralEditor] GameEditorManager missing.");
                        return;
                    }
                    mgr.OpenExclusive(ed);
                },
                isActive: () =>
                {
                    var ed = getter();
                    return ed != null && ed.IsActive;
                });
        }

        private static void TryQuickSave()
        {
            if (SaveService.Instance == null)
            {
                Debug.LogWarning("[GeneralEditor] SaveService unavailable — cannot quick-save.");
                return;
            }
            SaveService.Instance.QuickSave();
        }

        private static void QuitToMainMenu()
        {
            if (SaveService.Instance != null)
            {
                try { SaveService.Instance.QuickSave(); }
                catch (Exception ex)
                { Debug.LogError($"[GeneralEditor] Quicksave on quit failed: {ex.Message}"); }
            }
            SceneTransitionManager.LoadScene("MainMenu");
        }
    }
}
