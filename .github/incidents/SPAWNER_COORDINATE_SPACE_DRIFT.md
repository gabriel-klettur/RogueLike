# Spawners drift by their zone's origin on every restart

**Date:** 2026-08-19
**Status:** Fixed. Data repaired, no loss.
**Symptom as reported:** *"planto spawners y al reiniciar el juego no se quedan en el mapa"*

## What actually happened

The spawners saved correctly every single time. They came back somewhere else.

`spawners_instances.json` stores each position as a `tile` that is **zone-relative with the
row axis flipped** — row 0 is the *top* of the zone. `SpawnerInstanceLoader` knew that and
converted on the way in:

```csharp
worldX = zoneDef.gridOffset.x + tileCol;
worldY = zoneDef.gridOffset.y + (zoneHeightTiles - 1) - tileRow;
```

`SpawnerEditorManager.SaveInstancesToJson` did not, and wrote the position straight out:

```csharp
int col = Mathf.RoundToInt(pos.x);
int row = Mathf.RoundToInt(pos.y);
```

Absolute world coordinates going into a field that is read as zone-relative. Every save/load
cycle therefore displaced each spawner by its zone's origin. Lobby sits at `(150, 50)`:

| cycle | stored tile | loads to world |
|---|---|---|
| 0 | `(262, 78)` | `(412, 21)` |
| 1 | `(412, 21)` | `(562, 78)` |
| 2 | `(562, 78)` | `(712, 21)` |

150 tiles right per restart. After two or three the spawners were off the visible map, which
the user experienced as "they are gone".

## Why nothing caught it

Both halves were individually reasonable, and **nothing in the codebase ever compared them**.
A single save produced a well-formed file with plausible numbers. It took a *restart* to move
anything, and by then the file was already wrong — so the corruption and the symptom were
separated by an app lifetime.

The evidence had been sitting in the data the whole time. Entries read:

```json
{"tile": [412, 21], "id": "barbol_periodic_faster_Lobby_262_78"}
```

The `id` records the world position at placement and is stamped once and never rewritten, so
`id` and `tile` disagreeing by exactly the zone origin *is* the bug, written down, in the
shipped file.

## Two defects that made it worse

1. **`ResolveZone` was a stub** returning `"Lobby"` unconditionally, carrying a TODO asking for
   a lookup (`ZoneManager.TryGetZoneAtTile`) that already existed. Harmless while the zone was
   only a label — fatal once the save converted positions *through* that zone's origin, because
   a spawner placed in `zone_150_50` and stamped `Lobby` had its tile computed against the
   wrong offset.

2. **`ClearInstances` and the save disagreed about which spawners exist.** The loader tracked
   what it created in `_instances`; the F3 editor created spawners directly and never
   registered them. The save persisted by `FindObjectsOfType`, so editor-placed spawners *did*
   reach the file — but a reload destroyed only the tracked ones and then recreated everything
   from the file, so the map doubled on every reload. Once autosave landed, the doubling
   persisted instead of vanishing at the next restart: one id ended up in the file five times.

Also worth recording: the editor had **no automatic save at all**. Its only trigger was a
toolbar button, while every sibling editor took Ctrl+S or saved on close. That is what made the
report arrive as "persistence doesn't work" rather than "positions drift".

## The fix

- `Gameplay/Spawners/SpawnerTileMapping.cs` owns **both** directions. Loader and editor both
  call it, so they cannot disagree again.
- `ResolveZone` resolves the real zone from the world position and warns when a position falls
  outside every zone.
- `ClearInstances` enumerates `FindObjectsOfType<SpawnerInstance>()` — the same set the save
  enumerates.
- Autosave on every mutation, debounced 0.75 s, flushed on Ctrl+S, on close, and on quit, with
  a refusal to write an empty scene over a populated file.

## Data repair

27 entries recovered, **none lost**: 18 restored from a snapshot taken before any change (still
in the correct space), 9 rebuilt from the world coordinates their ids preserve. Snapshots of
the pre-dedupe and pre-repair states were kept.

## The hook this leaves behind

**A persistence round trip is a pair, and a test that only exercises one half proves nothing.**

Two guards now exist, and they catch the bug at different times:

- `SpawnerTileMappingTests` runs 25 save/load cycles and requires the spawner to be where it
  started. One round trip would have looked fine — the old code needed a restart to move
  anything — so the cycle count is the point, not decoration.
- `SpawnerFileIntegrityTests` reads the shipped JSON and asserts every tile is inside the zone
  it claims. This is the one that would have gone red **on the day the bug was written**,
  without anyone playing the game, because a tile of `412` in a 50-wide zone is self-evidently
  not zone-relative.

**If a similar symptom reappears in another subsystem** — placed content that saves fine and
comes back somewhere else — check the same three shapes before anything else:

1. Does the writer transform the coordinate the same way the reader untransforms it?
2. Is the context that transformation depends on (zone, slot, origin) resolved correctly on
   *both* sides, or stubbed on one?
3. Does a reload destroy the same set the save persists?

Buildings, lights, particles and tile overlays all persist positions through similar paths.
See also `BUILDINGS_SAVE_POSITION_COLLAPSE.md`, whose root cause is still unexplained and whose
symptom — positions collapsing on save — is in this family.
