# Buildings Editor — audit and hardening (2026-09-05)

> Measured in Play Mode at 1600×800 with 302 placed buildings and a catalog of
> **1474** templates (CLAUDE.md still says 1176 — the count grew with the later
> prop waves). Every number below was read off the live session through
> `execute_code`; the ones marked *after* were re-measured in a fresh session
> with the fixes compiled in.

## Scores

| Aspect | Before | After | What moved it |
| --- | --- | --- | --- |
| Functionality | 8 | 8 | Untouched: fill, erase, doors, colliders, undo. |
| Persistence | 8 | 8 | Untouched. |
| World rendering | 7 | 8 | `buildings audit` console command. |
| Tests | 7 | 8 | +21 tests (picker window, thumbnail cache). |
| Integration | 7 | 8 | 12 stale "F10" strings gone; tutorial step 0 fixed. |
| Architecture / code | 6 | 7 | Picker split into list + window; liveness generation. |
| UX / UI | 6 | 8 | "TOOLS" no longer "TOO"; wheel notch = one row; tint survives hover. |
| Picker rendering | 5 | 9 | Bilinear mip-mapped thumbnails, not point-sampled art. |
| Data scalability | 4 | 8 | 39 slots exist whether the catalog holds 300 or 3000. |
| Performance | 3 | 9 | See the table below. |

## Performance, measured

| Measure | Before | After |
| --- | --- | --- |
| `RefreshPicker()`, All tab | 772 ms, 53 MB, 4423 objects | 3.8 ms, 0 KB, 39 slots |
| Editor open, first time (includes BuildUI) | ≥ 2 × 772 ms + BuildUI | 250 ms |
| `RecomputeHoverStack`, cursor moving | 7.8 ms / frame | 0.38 ms / frame |
| `RecomputeHoverStack`, cursor still | 7.8 ms / frame | 0.016 ms / frame |
| Editor canvas graphics | 4689 | 376 |
| Frame: editor open idle vs Update off | +4.6 ms | +0.1 ms |
| Frame: hovering / scrolling the grid | +7.2 ms | +0.8 ms |
| Frame: Show Colliders on, steady state | — | +0.5 ms (1000 hosts) |

## What was wrong, and the shape of each

- **The picker was rebuilt in full on every interaction** — open (twice:
  `Activate` and `RestoreWorkspace`), every slot click, every drag start, every
  keystroke in the search box, every tab change. Same shape as the Items table
  freeze: the cost was volume, for a viewport that shows eighteen slots. Fixed
  by `BuildingsRuntimeEditor.Picker.Virtual.cs`: content sized to
  `rows × pitch`, no layout group, no size fitter, slots pooled and placed by
  index, selection repainted through `SetSlotTint`.
- **The hover scan called `FindObjectsOfType` every frame the cursor was over
  the world.** A cache existed (`GetCachedBuildings`) with an explicit
  `InvalidateBuildingCache()` that paint and erase called and place and the
  loader did not. `BuildingObject.LiveGeneration` (bumped in `OnEnable` and
  `OnDisable`) keeps the cache honest without a call at every site, and the
  scan is skipped outright when the cursor has not moved.
- **Picker icons were the production sprites, point-sampled.** The buildings
  atlas is `filterMode: Point` with no mipmaps — correct for the world, wrong
  for a 1024 px castle in a 64 px slot. `SpriteThumbnailCache` builds a
  bilinear, mip-mapped copy on the GPU (crop into a mip chain, then a trilinear
  reduction), lazily for the slots on screen, bounded at 512 entries. Sprites
  already at or under 128 px are returned as they are.
- **The selected slot's tint was written straight onto the Image**, which
  Unity's next transition overwrote — exactly the trap the doc comment on
  `EditorUIHelpers.SetSlotTint` records.
- **"TOOLS" shipped as "TOO".** The panel was 60 px wide and the chrome close
  button reserves 24 px on the right. `MODES_W` is 84 now, its own constant
  rather than the Tile editor's.

## Deliberately not done

- **The collider overlay stays one GameObject per cell.** Measured steady
  state with it on: +0.5 ms a frame for 1000 hosts. A single mesh per building
  would be cleaner, but three fixtures (`BuildingColliderDebugOverlay*Tests`)
  are written against the per-cell hosts and the frame cost does not justify
  rewriting them.
- **The big partials are mostly not split.** Lifecycle (509 lines), Doors
  (490), MapInteraction (429), Erase (406). Splitting for size alone is the
  highest-risk, lowest-value change in this editor. The one real misplacement
  was fixed: the confirm modal, tutorial, visibility toggle and per-frame
  outline updates that lived in `Persistence.cs` (612 lines) now sit in
  `BuildingsRuntimeEditor.Chrome.cs`, and Persistence holds persistence (457).
- **Raw colour literals.** 37 in the Buildings folder; exactly one matched a
  theme token (now converted). The rest are authored values and a rewrite
  would be guessing.
- **Hover help.** `UIHoverHelp` is typed on the Camera editor's `UIRefs`; a
  generic one is a UIKit change, not a Buildings one. The editor already has a
  tutorial overlay behind the `?` button.

## For CLAUDE.md (fold in when the file is next committed)

- **A picker slot's colour written onto its Image lasts until the Button's
  next transition.** `SetSlotTint` exists for this; the Buildings picker
  shipped the direct write anyway, and its selection highlight vanished on the
  first hover.
- **A point-filtered atlas cannot be down-sampled by an Image.** One texture,
  one filter mode, every sampler inherits it. A picker needs its own
  thumbnails (`SpriteThumbnailCache`) or its big sprites read as speckle.
- **A "cache of every X in the scene" needs a liveness signal, not an
  invalidation call.** `BuildingObject.LiveGeneration` — an invalidation the
  caller must remember was already shipped and already missing at two of four
  sites.
- **The catalog holds 1474 building templates**, not 1176.

## Colliders window — audit and hardening (2026-09-06)

Measured the same way, on the same world: 302 buildings, **249 of them CG**
(shared scope), painting a tree whose image fourteen instances share, with
Show Colliders on.

| Measure | Before | After |
| --- | --- | --- |
| Paint, per mouse-move sample, brush 1 | 32.3 ms | ~1.9 ms |
| Paint, per sample, brush 4 | 123.0 ms | 7.5 ms |
| Paint, per sample, brush 8 | 149.2 ms | 8.7 ms |
| Erase, per sample | 0.4 ms | 0.02 ms |
| Erase, mouse release | ~330 ms | 2.6 ms |
| Allocation per sample | 24.7 KB | 0 KB |
| Authoring save | 51 ms on every release | debounced, 1 s after the last stroke |

### What was actually slow

- **The stroke propagated to every CG sibling on every sample.** Each
  newly-solid cell materialised a collider tile and, with the overlay on, an
  overlay visual — on the active building and on all fourteen siblings,
  synchronously. None of the cost was in the grid maths: cloning the grid
  measured 0.007 ms, the physics sync 0.001 ms, the panel refresh 0.005 ms.
  Siblings now receive the stroke once, on release, as a delta of the cells
  that changed.
- **Tiles were addressed by child name.** `Transform.name` marshals a fresh
  managed string out of the native object, so the walk looking for a pooled
  tile cost **205 µs** on a building with 451 children, against 3.1 µs for the
  native `transform.Find` beside it — once per cell, per building, per stroke.
  And the child count only grows: a retired tile is renamed into a pool and
  never destroyed. `BuildingCollisionTileIndex` gives O(1) lookup and keeps the
  names the loader and the tests read.
- **Every release wrote all 302 instances.** Debounced to one write a second
  after the last stroke, flushed on deactivate, on map-slot change and in
  `OnDestroy`, which is what Play Mode stopping runs.
- **`_undo.Do` replayed the whole grid onto every sibling** on release —
  ~3 s. The command is recorded rather than executed: the active building
  already reflects the stroke and the siblings take the delta. Undo and redo
  still run the full snapshot, because they must restore anything.

### Still slow, and why

The first large paint stroke with Show Colliders on across many on-screen
shared instances still costs ~1.6 s on release. That is the overlay
materialising one GameObject with a SpriteRenderer and a LineRenderer per
newly-solid cell per visible sibling. It is amortised — the overlay pools its
visuals, so later strokes reuse them — and it is the price of showing the
change on fourteen buildings at once. Making it cheap means drawing the
overlay as one mesh per building instead of one object per cell, which three
fixtures currently assert against.

### Usability

- The grid resolution moved from the **Properties** panel into the
  **Colliders** panel. It is the topology every other control there paints
  into, and splitting them meant watching a number in a different window.
- The panel is resizable, like Buildings, Tile, Items, Particles and Spells.
- The scope button and the status line say how many buildings a stroke edits
  ("CG · 14 buildings"), instead of "all of type".
- One name for one thing: the button, the hint and the status all say
  **# Solid** and **. Walkable**, matching the two characters the grid file
  stores. It used to be "Erase" on the button, `Walk` in the enum and "." in
  the data.
- The hint is three readable lines at 10 pt instead of one dense line of six
  shortcuts at 9 pt.
