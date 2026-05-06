---
status: in-progress
last-updated: 2026-05-06
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

### Known limitation (closes in Phase A below)

Auto-save still routes every loader through `WorldId.Base`, so editing
buildings in slot `myMap` *writes back to the same file the default slot
reads*. Switching maps **looks** independent today, but creating new content
in a non-default slot pollutes default's data on disk. Phase A removes this.

---

## Phase A — Per-slot persistence routing

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

- [ ] `Scripts/Gameplay/Editors/Map/MapEditorMapSlots.cs` — `SlotToWorldId`.

A2 (loaders)

- [ ] `Scripts/Gameplay/World/Buildings/BuildingLoader.cs`
- [ ] `Scripts/Gameplay/Spawners/SpawnerInstanceLoader.cs`
- [ ] `Scripts/Gameplay/World/Lighting/WorldLightLoader.cs`
- [ ] `Scripts/Gameplay/Particles/ParticleInstanceLoader.cs`

A3 (editors)

- [ ] `Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.Persistence.cs`
- [ ] `Scripts/Gameplay/Editors/Spawners/SpawnerEditorManager.Persistence.cs`
- [ ] `Scripts/Gameplay/Editors/Lighting/LightingRuntimeEditor.Persistence.cs`
- [ ] `Scripts/Gameplay/Editors/Particles/ParticlesRuntimeEditor.Persistence.cs`

A4 (orchestration)

- [ ] `Scripts/Gameplay/Editors/Map/MapEditorManager.MapSlots.cs`
      → `OnSlotSwitched` helper, called from `BeginNewMap` + `LoadMapSlot`.

A5 (tiles)

- [ ] `Scripts/Gameplay/Editors/Tile/TileOverlayPersistence.Static.cs`
- [ ] `Scripts/Infrastructure/Persistence/Repositories/JsonFileTileOverrideRepository.cs`
      (already routes per-WorldId in writes — confirm reads).

B (builtins)

- [ ] `Scripts/Gameplay/Editors/Map/BuiltinMapSlots.cs` (new)
- [ ] `Scripts/Gameplay/Editors/Map/MapEditorUIBuilder.MapSlots.cs`
      (section header for builtins).

C (gameplay)

- [ ] `Scripts/Gameplay/World/Portals/WorldPortal.cs` (review existing)
- [ ] `Scripts/Gameplay/Save/SaveFileManager.cs` (per-world folder).
