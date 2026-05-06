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
using Valkur.Gameplay.MapEditor.Backups;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.Editors.Boss;
using Valkur.Gameplay.TimeWeather;
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

            // ── Editors ─────────────────────────────────────────────────────
            list.Add(MakeEditor("Tile",          () => TileEditorManager.Instance));
            list.Add(MakeEditor("Buildings",     () => BuildingsRuntimeEditor.Instance));
            list.Add(MakeEditor("Items",         () => ItemsRuntimeEditor.Instance));
            list.Add(MakeEditor("Spells",        () => SpellsRuntimeEditor.Instance));
            list.Add(MakeEditor("Entities",      () => EntitiesRuntimeEditor.Instance));
            list.Add(MakeEditor("Boss",          () => BossEditorManager.Instance));
            list.Add(MakeEditor("FSM",           () => FSMRuntimeEditor.Instance));
            list.Add(MakeEditor("Map",           () => MapEditorManager.Instance));
            list.Add(MakeEditor("Inventory",     () => InventoryRuntimeEditor.Instance));
            list.Add(MakeEditor("Particles",     () => ParticlesRuntimeEditor.Instance));
            list.Add(MakeEditor("Spawners",      () => SpawnerEditorManager.Instance));
            list.Add(MakeEditor("Lighting",      () => LightingRuntimeEditor.Instance));
            list.Add(MakeEditor("Time & Weather",() => TimeWeatherEditor.Instance));

            // ── Diagnostics (toggles, no exclusive activation) ──────────────
            list.Add(new GeneralEditorEntry(
                "Combat Ranges", GeneralEditorSection.Diagnostics,
                onClick:  () => CombatRangeVisualizer.Instance?.ToggleVisible(),
                isActive: () => CombatRangeVisualizer.Instance != null
                                && CombatRangeVisualizer.Instance.IsVisible));

            list.Add(new GeneralEditorEntry(
                "Debug HUD", GeneralEditorSection.Diagnostics,
                onClick:  () => ServiceLocator.Get<IDebugOverlayService>()?.ToggleVisible(),
                isActive: () => ServiceLocator.Get<IDebugOverlayService>()?.IsVisible == true));

            list.Add(new GeneralEditorEntry(
                "Save Log", GeneralEditorSection.Diagnostics,
                onClick:  Valkur.Gameplay.Save.SaveTelemetryHUD.Toggle,
                isActive: () => Valkur.Gameplay.Save.SaveTelemetryHUD.Instance != null));

            // ── Game ────────────────────────────────────────────────────────
            list.Add(new GeneralEditorEntry(
                "Pause Menu", GeneralEditorSection.Game,
                onClick: () => ServiceLocator.Get<IPauseMenuService>()?.OpenPause(),
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Save Game", GeneralEditorSection.Game,
                onClick: TryQuickSave,
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Load", GeneralEditorSection.Game,
                onClick: () => ServiceLocator.Get<IPauseMenuService>()?.OpenLoadGame(),
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Options", GeneralEditorSection.Game,
                onClick: () => ServiceLocator.Get<IPauseMenuService>()?.OpenOptions(),
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Map Backups", GeneralEditorSection.Game,
                onClick: OpenMapBackupBrowser,
                closesLauncher: true));

            list.Add(new GeneralEditorEntry(
                "Exit to Menu", GeneralEditorSection.Game,
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

        // Spawns the backup browser and wires its close hook to reopen the
        // General Editor — the user expects ESC inside the browser to take
        // them back to the launcher, not straight to gameplay.
        private static void OpenMapBackupBrowser()
        {
            var browser = MapBackupBrowserUI.Open();
            if (browser == null) return;
            browser.SetOnClose(() =>
            {
                var ge  = GeneralEditorManager.Instance;
                var mgr = GameEditorManager.Instance;
                if (ge != null && mgr != null) mgr.OpenExclusive(ge);
            });
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
