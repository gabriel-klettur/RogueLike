# Play Mode entered mid test run: the write guard switched off and the world was wiped

**Date:** 2026-09-14, 18:15–18:17 (local)
**Status:** Fixed (`WorldDataWriteGuard`), data restored.

## What happened

An EditMode run started at 18:15:38 from one Claude session. Another session sharing the same
Editor entered Play Mode during that run to take gameplay captures. Entering Play Mode runs every
`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hook, and
`WorldDataWriteGuard.ResetWorldDataWriteGuardStatics` set `s_testRunActive = false`. The guard's
whole rule is "refuse a write to the shipped path while a test run is active", so from that moment
the remaining fixtures wrote the real files. Nothing was refused and nothing was logged.

The run itself then died with `InvalidOperationException: This cannot be used during play mode.`
inside `EditModeRunnerCallback.RunFinished`, leaving an orphaned runner.

## What was destroyed

| File | Before | After |
|---|---|---|
| `StreamingAssets/Buildings/buildings_instances.json` (+ `.prev`) | 323 placements, 78 882 B | 1 placement, 262 B |
| `StreamingAssets/Buildings/buildings_collisions_by_building_instance_id.json` | 45 594 B | 168 B |
| `StreamingAssets/Buildings/buildings_collisions_by_image.json` | 20 832 B | 6 B |
| `StreamingAssets/Particles/particles_instances.json` | 188 emitters, 352 987 B | 476 B |
| `persistentDataPath/map_editor_zones.json` (+ `.bak`) | 45 zones incl. Lobby | 7 fixture zones, no Lobby |
| `StreamingAssets/Maps/default.zones.json` | its mirror | same corruption |

The MapBackups auto snapshot taken at 18:17:44 (on quit) captured the corrupted state, so "the
newest backup" was the wrong one to restore from.

## Recovery

- Buildings (three files) and particles: restored from `MapBackups/default_20260914_142609`, the last
  snapshot before the wipe. It is newer than `HEAD` (the buildings file carries uncommitted edits).
  The corrupted copies were kept in the restoring session's scratchpad.
- Zones: restored by another session from bytes verified identical across three independent copies
  (md5 `1430696b…`: a 14:36 snapshot, `Data/Backups/map_editor_zones.json.bak`, and
  `MapBackups/default_20260914_181720`).

## Fix

`s_testRunActive` is no longer reset on `SubsystemRegistration`. The flag belongs to
`WorldDataWriteGuardTestHook`'s run callbacks, which set and clear it; it is marked
`[SelfHealingStatic]` with that reason. A stale `true` after an orphaned run only refuses writes
until the next run or domain reload, which is the safe way for this flag to be wrong.
`WorldDataWriteGuardTests.EnteringPlayModeMidRun_DoesNotSwitchTheGuardOff` invokes the reset hook
from inside a run and asserts the run is still flagged.

Every guard built on that flag is covered by the same fix: `MapEditorMapSlots`,
`MirrorWorkingCopyToActiveSlot`, `WorldExcursion`, `SeedWorldLab`.

## Rule for shared-Editor sessions

Before entering Play Mode, check for a live runner:
`Resources.FindObjectsOfTypeAll(TestJobDataHolder)` → `TestRuns.Count == 0`. `run_tests`
answering `tests_running` is the same signal. And when restoring world data, do not trust the
newest snapshot: check its size against the last known good one first.
