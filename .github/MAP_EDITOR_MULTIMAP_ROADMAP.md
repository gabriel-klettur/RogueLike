---
status: phase-a-complete
last-updated: 2026-08-18
owner: Map Editor
---

# Map Editor — Multi-Map Roadmap

Brings the Map Editor (F11) from "one shared world with named zone snapshots" to
**multiple independent worlds** — Main / Sky / Hell / Dungeon / Custom — each
with its own buildings, NPCs, tiles, lights, particles and drops. Each map slot
becomes a true parallel dimension.

The infrastructure is mostly in place: `WorldId`, `IWorldManager`, and every
domain-specific repository (`IBuildingInstanceRepository`,
`ISpawnerInstanceRepository`, `ILightInstanceRepository`,
`IParticleInstanceRepository`, `IItemDropRepository`,
`ITileOverrideRepository`) **already accept a `WorldId` parameter**. What is
missing is the wiring that pipes the active map slot through these repos.

---

## What ships in this revision

1. **Slot-aware tile overrides at the visual level**
   `MapEditorManager.RefreshTilemapForActiveSlot()` clears the live tilemap and
   reapplies overrides that match the currently registered zones. Override
   files for *other* slots' zones are skipped (their zones are not in
   `ZoneManager`), so each map looks like a clean slate even though the
   override JSONs all share `MapOverrides/`.

2. **World-content swap on slot change**
   `BeginNewMap` → wipes spawned buildings, NPC spawners, lights.
   `LoadMapSlot` → wipes them and re-spawns from disk so switching back to a
   saved slot restores its content.

3. **Player teleport to slot origin**
   `TeleportPlayerToBlankMapOrigin()` warps the player to world `(0, 0)` and
   tells Cinemachine the target was teleported (no smooth lerp catch-up).

4. **`default` slot is protected** — never deletable, never renamable, always
   present in the saved-maps list.

### Known limitation (CLOSED — see "Phase A as shipped")

Auto-save used to route every loader through `WorldId.Base`, so editing
buildings in slot `myMap` *wrote back to the same file the default slot
reads*. Switching maps **looked** independent, but creating new content
in a non-default slot polluted default's data on disk.

Buildings were fixed first (via `MapEditorActiveSlot`); spawners, lights,
particles and authored item drops were closed on 2026-08-18. No world-content
domain shares a file across slots any more.

---

## Phase A — Per-slot persistence routing — ✅ SHIPPED (2026-08-18)

> **What actually shipped differs from the plan below, which is kept for
> historical context.** The plan gave every loader its own `_activeWorldId`
> field. That was rejected once the Buildings fix landed, because it would
> have written custom-slot data into `StreamingAssets/Worlds/<slug>/` — a
> read-only location on most build targets — and would have duplicated the
> same routing logic across five loaders and four editors.
>
> **Shipped design — routing lives in one place, not in every caller:**
>
> - `Valkur.Core.MapEditorActiveSlot` gained a generic `DirFor(subdir, slot)`
>   (`BuildingsDir` is now a thin wrapper over it). Default slot →
>   `StreamingAssets/<Subdir>/`; custom slot →
>   `persistentDataPath/Maps/<slot>/<Subdir>/`.
> - `WorldStreamingFileRepositoryBase` consults it for the base world behind an
>   opt-in `IsMapSlotAware` flag. `JsonFile{Spawner,Light,Particle}InstanceRepository`
>   and `JsonFileItemDropRepository` opt in; `JsonFileZoneDatabaseRepository`
>   deliberately does not (shared zone catalog).
> - A pinned `streamingRootOverride` (tests, run-scoped drop store) always wins
>   over slot routing, so a player's in-progress run drops stay in `Saves/<runId>/`.
> - The two callers that bypassed repositories were routed too:
>   `FileParticleInstanceStore` (resolves per call, not in its constructor) and
>   `SpawnerEditorManager`'s save.
> - Slot switch now flushes lights and item drops to the OUTGOING slot, and
>   clears + reloads buildings, spawners, lights, particles and drops for the
>   INCOMING one (`WorldLightLoader.Reload/ClearSpawnedLights`,
>   `ItemDropService.ReloadForActiveSlot` were added for this).
>
> Because the loaders resolve their file through the active slot at call time,
> **no loader needed an `_activeWorldId` field at all** — A2 and A3 below are
> satisfied without touching those files.
>
> Regression coverage: `Assets/Tests/EditMode/Editors/MapEditor/WorldContentPerSlotRoutingTests.cs`
> (24 cases) pins default-slot byte-compat, custom-slot isolation, the
> zone-database exemption, pinned-root precedence, and world-vs-slot orthogonality.
>
> The map slot and the `WorldId` axes stay orthogonal: slots are user-authored
> maps under `persistentDataPath`, worlds are designed dimensions that ship in
> `StreamingAssets/Worlds/<slug>/`. Phase B/C below still describe the world axis.

### Original plan (historical)

Wire each loader through the active slot's `WorldId`. Path mapping is already
implemented by `WorldStreamingFileRepositoryBase`:

| WorldId       | Disk path                                                       |
|---------------|-----------------------------------------------------------------|
| `Base`        | `StreamingAssets/<subdir>/<file>` (legacy)                      |
| Other slug    | `StreamingAssets/Worlds/<slug>/<subdir>/<file>`                 |

### A1. Slot ↔ WorldId map
Add to `MapEditorMapSlots`:

```csharp
public static WorldId SlotToWorldId(string slot)
{
    if (string.IsNullOrEmpty(slot) ||
        string.Equals(slot, DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase))
        return WorldId.Base;                                  // legacy mapping
    string slug = Sanitize(slot).ToLowerInvariant();
    return new WorldId(WorldDescriptor.DeterministicGuid(slug), slug);
}
```

### A2. Loaders adopt an `_activeWorldId` field

Each loader replaces `ReadRawJson(WorldId.Base)` /
`WriteRawJson(WorldId.Base, …)` with a private `_activeWorldId` and exposes
`SetActiveWorldId(WorldId)`.

Files to touch:

- `Scripts/Gameplay/World/Buildings/BuildingLoader.cs` (line 124)
- `Scripts/Gameplay/Spawners/SpawnerInstanceLoader.cs` (~line 89)
- `Scripts/Gameplay/World/Lighting/WorldLightLoader.cs` (~line 286, 408)
- `Scripts/Gameplay/Particles/ParticleInstanceLoader.cs` (load + save sites)
- `Scripts/Gameplay/WorldDrops/ItemDropService.cs` (already takes a `WorldId`
  in its constructor — wire its update path).
- `Scripts/Gameplay/Editors/Tile/TileOverlayPersistence.Static.cs` —
  `ApplyAllOverrides` is the **only** static reader still ignoring `WorldId`;
  give it a `WorldId` parameter.

### A3. Editors save through the active slot

Today `BuildingsRuntimeEditor.SaveInstancesToJson()` (line 53) writes a
hardcoded `streamingAssetsPath/Buildings/buildings_instances.json`. Replace
the hardcoded path with `_repository.WriteRawJson(_activeWorldId, json)`.

The other in-game editors (Spawners F3, Lighting Ctrl+F3, Particles F1) all
go through their loaders' `Save…()` methods today; once the loaders adopt
`_activeWorldId` (A2) those editors are slot-aware automatically.

### A4. Slot-switch pipeline

`MapEditorManager.MapSlots.cs` (already partial-class friendly):

```csharp
private void OnSlotSwitched(WorldId previous, WorldId next)
{
    // 1. Make sure dirty buffers in each editor are flushed to OLD slot.
    BuildingsRuntimeEditor.Instance?.FlushDirtyChanges();
    SpawnerEditorManager.Instance?.FlushDirtyChanges();
    // … repeat for Particles / Lighting

    // 2. Clear all spawned scene objects.
    ClearAllSpawnedWorldContent();

    // 3. Point every loader at the new world.
    FindObjectOfType<BuildingLoader>()?.SetActiveWorldId(next);
    FindObjectOfType<SpawnerInstanceLoader>()?.SetActiveWorldId(next);
    FindObjectOfType<WorldLightLoader>()?.SetActiveWorldId(next);
    FindObjectOfType<ParticleInstanceLoader>()?.SetActiveWorldId(next);

    // 4. Re-spawn from new slot's files.
    ReloadAllWorldContent();
}
```

Call `OnSlotSwitched(prevWorldId, MapEditorMapSlots.SlotToWorldId(slot))` at
the end of `BeginNewMap` and `LoadMapSlot`.

### A5. Tile overrides

`TileOverlayPersistence` already has a per-`WorldId` instance constructor;
the gap is the static `ApplyAllOverrides` path. Add the `WorldId` parameter
and read overrides from
`Application.persistentDataPath/MapOverrides/<slug>/`. Slot `default` keeps
the legacy flat folder. `MapEditorManager.RefreshTilemapForActiveSlot` then
calls the new slot-aware overload.

---

## Phase B — Built-in worlds (Main / Sky / Hell)

Once Phase A lands, builtin worlds are pure data:

```csharp
public static class BuiltinMapSlots
{
    public const string Main = "default";  // backwards-compatible alias
    public const string Sky  = "sky";
    public const string Hell = "hell";
}
```

Auto-create the slot files on first launch with an empty
`ZonePersistenceFile`, lock them against delete/rename the same way `default`
already is, and surface them at the top of the saved-maps list with a
section header (a `BuildSectionLabel` in the UI builder, then the user-created
slots underneath). UX-parity with how the other editors group "builtin" vs.
"custom" entries.

Optional flavour:

- Per-slot ambient lighting preset (Sky → bright/blue, Hell → red/dim,
  Main → daylight cycle as today).
- Per-slot music override on `ZoneDefinition.zoneMusic` defaults.
- `WorldPortal` already raises `IWorldManager.ActiveWorldChanged`. The
  Map Editor slot switch becomes a regular consumer of that event, so
  in-world portal NPCs and the editor stay in sync automatically.

---

## Phase C — Cross-world traversal & persistence at runtime

Phase A/B treat slot-switch as an **editor-time** operation. To make Sky/Hell
real *gameplay* dimensions:

1. **`WorldPortal` GameObject** — physical doorway prefab placed via the
   Buildings Editor that triggers `IWorldManager.ActivateAsync(targetWorldId)`
   on player overlap, then teleports the player to the configured spawn point
   in the destination world.
2. **Per-world save folders** — `SaveFileManager` already keys on
   `Active.WorldId` (see `WorldManager.cs` doc-comment); confirm and wire.
3. **NPC pursuit boundaries** — extend `AlertChaseState` to drop aggro when
   the player crosses a world boundary (otherwise enemies attempt to chase
   into a dimension they don't exist in).

---

## File-by-file checklist

A1 (slot ↔ WorldId)

- [x] `Scripts/Gameplay/Editors/Map/MapEditorMapSlots.cs` — shipped as
      `ResolveWorldId` / `ResolveBootActiveWorldId`.
- [x] `Scripts/Core/MapEditorActiveSlot.cs` — `DirFor(subdir, slot)` (the
      routing primitive the shipped design is built on).

A2 (loaders) — no per-loader field needed; routing centralised in the repos

- [x] `Scripts/Infrastructure/Persistence/Repositories/WorldStreamingFileRepositoryBase.cs`
      — `IsMapSlotAware` opt-in; pinned root still wins.
- [x] `JsonFile{Spawner,Light,Particle}InstanceRepository`, `JsonFileItemDropRepository`
      — opted in.
- [x] `Scripts/Gameplay/VFX/FileParticleInstanceStore.cs` — bypassed the repo
      layer; now resolves its path per call through the active slot.

A3 (editors)

- [x] `Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.Persistence.cs`
      — already slot-aware via `MapEditorActiveSlot`.
- [x] `Scripts/Gameplay/Editors/Spawners/SpawnerEditorManager.Modes.cs` — the
      save path was the last raw `Application.streamingAssetsPath` write.
- [x] Lighting (Ctrl+F3) and Particles (F1) editors — save through
      `WorldLightLoader.SaveAll` / `FileParticleInstanceStore`, both now routed.

A4 (orchestration)

- [x] `Scripts/Gameplay/Editors/Map/MapEditorManager.MapSlots.cs` — flush
      (tiles, buildings, lights, drops) before the pointer flips; clear +
      reload every domain after. Shipped inside the existing
      `BeginNewMap` / `LoadMapSlot` flows rather than as a separate
      `OnSlotSwitched` helper.
- [x] `WorldLightLoader.Reload()` / `ClearSpawnedLights()` and
      `ItemDropService.ReloadForActiveSlot()` added to make that possible.

A5 (tiles) — was already done before this pass

- [x] `Scripts/Gameplay/Editors/Tile/TileOverlayPersistence.Static.cs` — takes a
      `WorldId`; the legacy no-arg overloads default to `WorldId.Base`.
- [x] `Scripts/Infrastructure/Persistence/Repositories/JsonFileTileOverrideRepository.cs`
      — reads and writes both route per-`WorldId`.

B (builtins)

- [ ] `Scripts/Gameplay/Editors/Map/BuiltinMapSlots.cs` (new)
- [ ] `Scripts/Gameplay/Editors/Map/MapEditorUIBuilder.MapSlots.cs`
      (section header for builtins).

C (gameplay)

- [ ] `Scripts/Gameplay/World/Portals/WorldPortal.cs` (review existing)
- [ ] `Scripts/Gameplay/Save/SaveFileManager.cs` (per-world folder).
