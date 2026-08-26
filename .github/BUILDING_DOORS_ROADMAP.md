# Building Doors & Interiors — Audit, Shipped Work, and Roadmap

> Audited 2026-08-26. Buildings scored **0/10** on "the player can enter one": no door
> concept existed anywhere in the data, the loader, or the editors. Phases 1–3 shipped the
> same day. A player can now walk into a house and walk back out, and a designer can author
> that from inside the game.

## 1. What buildings were

A building was a cropped sprite plus a collision footprint. Nothing else.

| Layer | File | What it owns |
| --- | --- | --- |
| Data | `Data/World/BuildingTemplateData.cs` | `assetPath`, `solid`, `splitRatio`, `colliderScope`, `originalScale`, `lightPresetKey` |
| Runtime | `Gameplay/World/Buildings/BuildingObject.cs` | Splits the sprite into `Footprint` (WallsBottom) and `Canopy` (WallsTop); one `BoxCollider2D` or N `CollTile_r_c` |
| Collision | `Gameplay/World/Buildings/BuildingCollisionLoader.*.cs` | A `#`/`.` char matrix, per-image (CG) or per-instance (CU) |
| Placement | `Gameplay/World/Buildings/BuildingLoader.*.cs` | Reads `buildings_instances.json`, converts `(zone, rel_x, rel_y)` to a bottom-center world position |
| Authoring | `Gameplay/Editors/Buildings/BuildingsRuntimeEditor.*.cs` (F10) | Place / drag / resize / paint colliders / save |

## 2. Where the door data lives, and why not in the collision grid

The obvious idea is a new glyph (`D`) in the `#`/`.` matrix the F10 editor already paints.
It is wrong, and it fails silently:

`BuildingCollisionLoader.ResampleGrid` collapses every destination cell to a single `bool
solid` OR-ed over its source cells. A `D` glyph would be **erased the moment the instance
carries a `scale` override** — which most of the shipped data does — and a `D` next to a `#`
would be swallowed even without a resample. Nothing throws; the door just stops existing on
exactly the buildings a designer resized.

So the split is:

- **Where the door is** — a property of the *art*, identical for every placement of that
  template. Stored on `BuildingTemplateData` as a **normalized** offset + size, exactly like
  `lightOffsetNormalized`. Normalized survives `scale` overrides, `splitRatio` changes and
  grid resampling by construction.
- **Where the door leads** — a property of the *placement*; every house needs its own
  interior. Stored per instance in `buildings_instances.json` under `overrides.door`, so it
  travels with the building on move, copy and delete.

```json
{"id": 64, "template_id": 307, "zone": "zone_100_50", "rel_x": 989, "rel_y": 915,
 "overrides": {"door": {"target": "Interiors/house_interior_small.overlay.json",
                        "spawn_x": 7.500, "spawn_y": 5.500}}}
```

## 3. How the player gets in

**By walking in, not by pressing a key**, and detection is a **poll, not a trigger**.

- The doorway sits inside the building's own solid footprint, so the only way to reach it is
  to aim at it. The usual objection to walk-on triggers does not apply here.
- A key press is blocked anyway: `ValkurInputActions` binds `<Keyboard>/e` to BOTH `Interact`
  and `SpellSlash`, so a key-press door would cast a slash on every entry — and nothing in
  the game reads `InputService.Gameplay.Interact`. `NPCInteractable.Interact()` has no caller
  either. That interaction path is dead wiring, not a system a door could plug into.
- Buildings carry no `Rigidbody2D`, so a doorway trigger would depend entirely on the
  player's Dynamic body to raise the contact — and a Dynamic body that comes to rest goes to
  sleep (`Player.prefab` ships Sleeping Mode = Start Awake, Time To Sleep = 0.5 s). **A
  sleeping body starts no new contacts.** `ResurrectionZone` already polls a building
  footprint for the same class of reason.

## 4. The way out

`WorldTransitionService.EnterOverlay` drops an `InteriorExit` **on the tile the player
arrives on**, and that removes the authoring burden entirely: an interior is a hand-drawn
tile matrix with no components in it, so any design that asks an author to also place an exit
produces a room somebody is trapped in the first time they forget.

Arriving on your own exit is the natural reading of a doorway — you come in through the door,
and the door is behind you. It is therefore **disarmed until the player has stepped away**
(`ARMING_DISTANCE_WORLD`, deliberately larger than the exit rect so shuffling on the spot
cannot arm and re-enter in one motion).

A refused trip back **re-arms the exit and puts the return point back**. A spent exit in a
sealed room is a soft-lock.

## 5. What a world swap actually is

The original two-line implementation (`ClearWorld` + `LoadOverlay`) was wrong in five ways
that only surface end to end. All five are fixed in `WorldTransitionService`:

| Failure | Why it happened | Fix |
| --- | --- | --- |
| Buildings, lights, spawners and emitters float over the interior, walls and all | `WorldGridBuilder.ClearWorld` calls `ClearAllTiles` and destroys nothing else | `ClearBaseWorldContent` / `ReloadBaseWorldContent` via `MapEditorManager`, the same entry points `reloadworld` uses |
| A typo'd destination leaves the player in a black void with no way back | the world was cleared before the destination was known to load | `IsOverlayLoadable` is checked BEFORE anything is torn down |
| The interior reports (and plays the music of) an outdoor zone | `ZoneManager.Update` re-detects a base-world zone from interior coordinates and overwrites `ForceZoneName` on the next frame | `ZoneManager.SuspendDetection` / `ResumeDetection` |
| The player arrives still walking, or asleep | a teleport writes `transform.position` and never touches the body | velocity zeroed and `WakeUp()` on every teleport |
| An editor autosave inside an interior wipes the world's authored content | the scene legitimately holds none of it, and count-based anti-wipe guards read that as "the author deleted everything" | `IsBaseWorldContentSuspended` + `RefuseWorldContentWrite`, checked by the Buildings and Particles save paths |

That last one is not hypothetical: it cost 188 placed particle emitters during this work,
restored from git. Their own guard caught most writes and inferred wrongly on one.

## 6. Where interiors live

`StreamingAssets/Maps/Interiors/`, **not** beside the zone overlays.

Everything directly under `Maps/` is a 50×50 tile of the base world — `WorldLoader` composes
them by offset and `RealShippedOverlayBoundsAndNamesTests` asserts that exact size for every
file it finds there. A 14×10 room in that folder either breaks the invariant or forces it to
grow an exception list, and an invariant with exceptions stops catching the malformed zone it
exists to catch. The first interior was moved rather than the rule loosened.

Generate one with:

```bash
python tools/maps/generate_interior_overlay.py --width 20 --height 14 --name inn
```

## 7. Authoring a doorway

### In game, F10 → Door

Toolbar button `Door`, then click a building. The flyout separates the two scopes explicitly,
because one of them edits shared catalog data:

- **Template (all placements)** — `[X] Has doorway`, `Anchor X`, `Anchor Y`, `Size`. These
  write the `BuildingTemplateData` asset and move the doorway on *every* placement of that art.
- **This placement** — target overlay, spawn X/Y, `Apply` / `Clear`. These write
  `overrides.door` through the same `ExecutePersistedEdit` every other F10 edit uses.

A live yellow (or green, once it leads somewhere) rect is drawn on the doorway while the mode
is open.

### From the dev console

```text
overlays                                        # what a doorway can target
door                                            # inspect the building nearest the player
door id 64                                      # inspect one explicitly
door on                                         # template: this art has a doorway
door Interiors/house_interior_small.overlay.json 7.5 5.5
door anchor 0.5 0.06                            # template: move it
door size 0.18                                  # template: resize it
door clear                                      # this placement leads nowhere again
doors                                           # every doorway in the world
door enter                                      # walk through it without walking
leave                                           # come back out
```

`door id <n>` exists because the player is a Dynamic body and a solid footprint pushes them
back out, so "stand on it and type `door`" is not reproducible for a scripted run.

Every command routes through the same `BuildingsRuntimeEditor.TrySetDoor` seams the F10 panel
uses. A console path with its own serializer would be a second writer to keep in step with
the reader — the exact shape of the spawner coordinate-space drift incident.

## 8. Shipped

| Phase | State |
| --- | --- |
| 1 — data, geometry, runtime doorway, persistence pair | **done** |
| 2 — F10 Door mode | **done** |
| 3 — interiors as content, exit, world-swap correctness | **done** for single-room interiors |
| 4 — interaction pass (`E` double-binding, revive `Interact`, prompts) | open — needs an owner decision on rebinding a combat key |
| 5 — in-place interiors (canopy cutaway, no load) | open |

**Working example shipped**: building ID 64 (`Buildings/houses/curse_house_topdown`, zone
`zone_100_50`) leads to `Interiors/house_interior_small.overlay.json` and lands the player at
(7.5, 5.5). Verified live: 170 buildings → 0 inside → 170 back, player returned to (237.56,
56.34) just outside the doorway, zone detection resumed, zero console errors.

## 9. Still open

- **Interiors are single rooms.** No furniture, no NPCs, no loot, no lighting of their own.
  They are a floor, four walls and a rug.
- **One interior file per doorway.** Two houses pointing at the same file share a room.
- **No nesting.** The return point is overwritten, not stacked. Nothing nests two deep today.
- **Phase 4 blocks prompts.** Until the `E` binding is resolved there is no "press to enter",
  no locked doors and no quest gating.
- **A doorway anchored on a painted-solid cell** is reported at load
  (`BuildingCollisionLoader.WarnIfDoorwayIsBlocked`) but not repaired — silently clearing
  authored collision behind the author's back would be worse.

## 10. Test inventory

| Fixture | Mode | Pins |
| --- | --- | --- |
| `BuildingDoorGeometryTests` | Edit | Anchor math, **scale invariance**, clamping, minimum extent, exit point, grid-cell mapping |
| `BuildingDoorSpecTests` | Edit | Validity rules, clone independence |
| `BuildingDoorFactoryTests` | Edit | Both halves required, child reuse, no stray collider, entry slack |
| `BuildingDoorPersistenceRoundTripTests` | Edit | The writer's exact bytes fed to the real parser, comma-decimal locale, escaping |
| `BuildingDoorEntryTests` | Edit | A refused entry leaves no armed return point; the doorway follows its building |
| `BuildingDoorAuthoringTests` | Edit | The `TrySetDoor` seams: every refusal, undo, template scope reaching every placement |
| `WorldTransitionServiceTests` | Edit | Return point, static reset, transition refusals, **write suspension** |
| `InteriorExitTests` | Edit | Arming, geometry, refused trip back re-arms |
| `InteriorOverlayContentTests` | Edit | The shipped room is sealed, walkable, and its spawn is in bounds |
| `BuildingDoorTransitionPlayTests` | Play | A real swap: tiles painted, velocity zeroed, exit dropped, armed on walk-away, return trip, refusal leaves the world untouched |
