using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>reload</c> command family: re-read authored data and rebuild the live
    /// scene from it, without leaving Play Mode.
    ///
    /// Editing a building, a light, an FSM state or a monster's stats used to cost a
    /// full Stop → recompile → Play → walk-back-to-the-test-zone cycle of 30–60 s,
    /// even though nothing about the C# had changed. Almost every loader already had
    /// a working reload path; nothing exposed it. These commands are the exposure.
    ///
    /// They are deliberately thin. Each one calls the same production entry point the
    /// editors already use, so a reload behaves exactly like the real load rather than
    /// becoming a second, subtly different code path that drifts.
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "reload"
    /// category, and reachable programmatically through <see cref="DevConsole.Execute"/>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterReloadCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "reloadworld",
                Aliases = new[] { "rw" },
                Usage = "reloadworld",
                Help = "re-read buildings / spawners / lights / particles / item drops for the active map slot",
                Category = "reload",
                Handler = _ => CmdReloadWorld()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "reloadfsm",
                Usage = "reloadfsm",
                Help = "invalidate the FSM cache and rebuild every live monster's state machine",
                Category = "reload",
                Handler = _ => CmdReloadFsm()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "reloadtiles",
                Aliases = new[] { "rt" },
                Usage = "reloadtiles",
                Help = "repaint the tilemap from Maps/Collisions JSON and re-bake colliders",
                Category = "reload",
                Handler = _ => CmdReloadTiles()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "map",
                Usage = "map [slot]",
                Help = "list map slots, or hot-load one (no argument reloads the active slot)",
                Category = "reload",
                Handler = CmdMap,
                Completer = _ => FindObjectOfType<MapEditorManager>()?.ListMapSlots() ?? Array.Empty<string>()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "reconfig",
                Usage = "reconfig",
                Help = "re-apply MonsterDefinition changes to living NPCs, keeping their positions",
                Category = "reload",
                Handler = _ => CmdReconfig()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "respawnnpcs",
                Usage = "respawnnpcs",
                Help = "kill every NPC and re-fire the spawners from a clean slate",
                Category = "reload",
                Handler = _ => CmdRespawnNpcs()
            });
        }

        // ── Handlers ─────────────────────────────────────────────────────────────

        private void CmdReloadWorld()
        {
            var manager = FindObjectOfType<MapEditorManager>();
            if (manager == null)
            {
                Log("[reload] No MapEditorManager in the scene — world content cannot be reloaded.");
                return;
            }

            manager.ClearAllSpawnedWorldContent();
            manager.ReloadAllWorldContent();
            Log($"[reload] World content reloaded for slot '{manager.ActiveMapSlot}'.");
        }

        private void CmdReloadFsm()
        {
            FSMRuntimeFactory.InvalidateCache();

            int rebuilt = 0;
            // Snapshot first: Initialize can touch the registry, and iterating a
            // collection while it mutates is how "reload" turns into a crash report.
            var monsters = new List<GameObject>(EntityRegistry.Monsters);
            foreach (var go in monsters)
            {
                if (go == null) continue;
                var brain = go.GetComponent<FSMMonsterBrain>();
                if (brain?.Definition == null) continue;
                brain.Initialize(brain.Definition);
                rebuilt++;
            }

            Log($"[reload] FSM cache invalidated, {rebuilt} monster brain(s) rebuilt.");
        }

        private void CmdReloadTiles()
        {
            var grid = FindObjectOfType<WorldGridBuilder>();
            var world = FindObjectOfType<WorldLoader>();
            if (grid == null || world == null)
            {
                Log("[reload] WorldGridBuilder or WorldLoader missing — cannot repaint tiles.");
                return;
            }

            TerrainCatalogLoader.InvalidateCache();
            grid.ClearWorld();
            world.LoadFullWorld();

            if (WorldCollisionBaker.HasInstance)
                WorldCollisionBaker.Instance.RebuildAll();

            Log($"[reload] Tiles repainted: {world.OverlaysLoaded} overlay(s), " +
                $"{world.CollisionsLoaded} collision grid(s), colliders re-baked.");
        }

        private void CmdMap(string[] args)
        {
            var manager = FindObjectOfType<MapEditorManager>();
            if (manager == null)
            {
                Log("[reload] No MapEditorManager in the scene.");
                return;
            }

            if (args == null || args.Length < 2)
            {
                Log($"[reload] Active slot: {manager.ActiveMapSlot}");
                foreach (var slot in manager.ListMapSlots())
                    Log("   " + (slot == manager.ActiveMapSlot ? "* " : "  ") + slot);
                Log("Use 'map <slot>' to hot-load one.");
                return;
            }

            string target = args[1];
            Log(manager.LoadMapSlot(target)
                ? $"[reload] Loaded map slot '{target}'."
                : $"[reload] Failed to load map slot '{target}' — does it exist? Run 'map' to list.");
        }

        private void CmdReconfig()
        {
            int reconfigured = 0;
            var monsters = new List<GameObject>(EntityRegistry.Monsters);
            foreach (var go in monsters)
            {
                if (go == null) continue;
                var def = go.GetComponent<FSMMonsterBrain>()?.Definition;
                if (def == null) continue;
                EntitySetup.ConfigureMonster(go, def);
                reconfigured++;
            }

            Log($"[reload] {reconfigured} NPC(s) reconfigured in place — positions preserved.");
        }

        private void CmdRespawnNpcs()
        {
            // Order matters. Monsters are parented to the [Entities] container, not to
            // the spawner that made them, so re-running the spawners does NOT remove the
            // old ones — skip the kill and you get duplicates instead of a clean slate.
            CmdKillAll();

            var loader = FindObjectOfType<SpawnerInstanceLoader>();
            if (loader == null)
            {
                Log("[reload] No SpawnerInstanceLoader — NPCs killed but none respawned.");
                return;
            }

            loader.LoadInstances();
            Log("[reload] NPCs cleared and spawners re-fired.");
        }
    }
}
