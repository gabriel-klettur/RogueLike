# Spawners — audit and roadmap

Audited 2026-09-07 at **4.1/10**. Steps 1–8 of the build order shipped the same day; the
score after them is **7.6**, and what caps it is the last step, the catalogue cleanup, which
is deliberately left until the migrated data has been looked at.

Axes that moved: instance data model 2 → 8, properties panel 2 → 8, catalogue duplication
2 → 5 (the DUPLICATION is now unnecessary rather than removed), coupling to the AI 1 → 6,
on-map feedback 4 → 7, picker 4 → 6, encounter difficulty 7 → 9, observability 2 → 7, inert
fields 3 → 10, authoring a new behaviour 1 → 6. Persistence stayed at 8 and gained one write
path instead of two. Tests 7 → 8.

Everything below the "What shipped" section is the original audit, unedited — the
measurements are what the design was argued from and are worth keeping in that form.

The finding that frames everything else: **a placed spawner is not an object, it is a
pointer.** Its entire on-disk record is four fields, and every decision about what it does
lives in a ScriptableObject shared with every other placement of the same kind. So the only
way to express a new behaviour is a new asset — which is why the catalogue holds twenty-five
templates, six of them byte-identical except for one string, seven never placed at all, and
several named after their own parameter values.

This is the same defect the Particles editor had and already fixed. The remedy below is that
fix, applied here: **copy on place**.

## What shipped (2026-09-07)

Copy-on-place, and the seven things it unblocked.

| Piece | Location | What it owns |
|---|---|---|
| `SpawnerInstanceConfig` | `Data/World/` | One placement's own behaviour. The only thing the runtime reads |
| `SpawnerInstanceRecord` | `Gameplay/Spawners/` | One parsed row of the file, including rows the loader could not spawn |
| `SpawnerInstanceSerializer` | `Gameplay/Spawners/` | Both directions of the file, plus the v1 → v2 freeze. Schema v2 |
| `SpawnBrain` | `Gameplay/Enemies/` | What the ENCOUNTER says about a spawn: FSM set, leash. Stamped like `SpawnLevel` |
| `SpawnerEditorUIBuilder.Rows` | `Gameplay/Editors/Spawners/` | The enum choice row, the roster row, the reapply pair |
| `DevConsole.Commands.Spawners` | `Gameplay/Bootstrap/` | `spawners [fragment]` — the probe the layer never had |
| `SpawnerInstanceMigrationTool` | `Editor/Spawners/` | `Valkur > Spawners > Migrate Instances To v2`, with a dry run |

Rules that came out of building it, and that the next change has to keep:

- **The omission is measured against the CLASS default, never against the preset's value.**
  Omitting fields that merely agree with the preset would let a later preset edit reach back
  into every placement that happened to match — the coupling copy-on-place removes, coming
  back in through the file. That makes the field initializers on `SpawnerInstanceConfig` and
  `SpawnerTemplateData` **one contract, not two declarations that agree**: if they drift, a
  value equal to the template default is written as nothing and read back as the CONFIG
  default, and the placement changes behaviour on its next load with nothing logged.
  `SpawnerInstanceConfigDefaultsTests` pins the pair, and pins that every preset field has a
  counterpart — the half that catches the NEXT field, since a pair test cannot see a field
  nobody listed.
- **The roster is DEEP copied.** A shallow copy leaves every placement pointing at the
  preset's own `WaveDefinition` objects, so editing one camp's roster edits them all — copy-
  on-place defeated one level down, and invisible until two placements of one preset exist.
- **The write-back is not optional, and it emits what was READ.** Freezing in memory alone
  lasts one session, so retuning a preset and restarting would re-snapshot every un-migrated
  placement from the new values. It writes every row it parsed, including the ones it refused
  to spawn (missing preset, unregistered zone), which is also why it needs no anti-wipe guard
  of its own: it cannot write fewer rows than it read.
- **A row whose preset is missing is left at v1, deliberately.** Freezing it against a blank
  preset would replace an author's data with defaults, in silence, on exactly the rows a
  broken catalogue reference already makes hard to notice.
- **`ApplyConfig` re-seeds the state machine, and does not withdraw what is already out.**
  Flipping Trigger from Proximity to Auto has to start the spawner or the control reads as
  broken; retracting live monsters because somebody widened a radius would be far stranger to
  watch than a camp whose next wave differs from its last.
- **`SpawnBrain` sits between `by_eid` and `by_archetype`.** Below `by_eid`, which is a
  hand-authored statement about one specific placement and the more specific claim. Above
  `by_archetype`, because otherwise a camp asking for `Monster_Caster` is silently overruled
  by the archetype mapping — which every shipped monster has, so the override would do
  nothing at all and would be the thirteenth authored-and-inert field in this subsystem. A
  name no set declares warns once and falls through rather than failing the spawn.
- **The brain is passed INTO `SpawnEntity`, not applied to its return value.**
  `EntitySetup.ConfigureMonster` runs inside that call and is where `FSMMonsterBrain` builds
  the machine, so a stamp applied afterwards is read by nobody. Same ordering constraint
  `SpawnLevel` carries, and the reason both travel as parameters.
- **The leash override is published AFTER `PublishBehaviourTuning`.** A monster whose asset
  authors its own `leashRange` would otherwise silently ignore the camp it was placed in, and
  the failure is invisible: both values are plausible and only their order decides.
- **The twelve inert fields are deleted, not carried into v2.** `spawnerType`,
  `randomSpawnRadius`, `defendSpawn`, `defendLeash`, `visibleInGame`, `proximityInitialOnly`,
  `damageable`, `maxHp`, `flashOnHit`, `flashColor`, `flashDurationSeconds`,
  `hpResetOnEnter` — plus `SpawnerDefinition.cs`, a whole ScriptableObject class with zero
  code references and zero assets. Snapshotting twelve dead fields into every placement is
  what deferring the cleanup past the schema freeze would have cost. Two of them came back as
  real features instead: `proximityInitialOnly` is now `proximityRearms` with an actual
  re-arm, and the defend pair is now `defendLeashRadius` delivered through `SpawnBrain`.
- **The re-arm has hysteresis and runs outside the state switch.** A camp that re-armed the
  instant the player crossed back out would fire again on the next step, so the re-arm ring is
  1.35x the fire ring; and a fired spawner is in Active/WaitClear/Done rather than Idle, so a
  re-arm folded into `UpdateIdle` could never run.
- **Two rings are drawn now, in two colours.** The trigger ring is where the player sets the
  camp off; the spawn ring is where bodies land and therefore what has to be clear of walls.
  Two rings in one hue read as a single thick band at authoring zoom.
- **One write path.** The editor's save and the loader's migration both go through
  `SpawnerInstanceSerializer` and both write through `ISpawnerInstanceRepository`, whose write
  is atomic and map-slot aware. The editor used to hand-roll its own `File.WriteAllText`
  against a separately-resolved path.

Still open, and why:

- **The catalogue cleanup (step 9).** Twenty-five presets collapse to about six once the
  placements are self-sufficient, and the seven orphans stop having a reason to exist. Left
  until the migrated file has been read by a human, because deleting presets and rewriting
  instances in one pass is two irreversible things at once.
- **A collider check at placement time.** The check has to be an `OverlapCircle` against
  World|Building in Play Mode — the collision grid covers terrain only and building colliders
  are baked at runtime — so it is a runtime probe rather than a test, and it wants the
  editor's placement path rather than the serializer.
- **Saving a placement AS a new preset.** `UpsertTemplate` still has no runtime caller. It
  hurts far less now that a knob change costs nothing, but it is the step that closes the loop.
- **The stale instance ids.** Six of twenty shipped rows encode a tile they no longer sit on,
  because the id is minted at placement and never re-minted. Harmless while nothing parses an
  id; part of step 9.

## What was measured

All numbers were read out of the shipped assets and the shipped JSON, not estimated.

| Axis | Score | Finding |
|---|---|---|
| Instance data model | 2 | On-disk record is `template_id` / `zone` / `tile` / `id`. Zero overrides |
| Inert fields | 3 | 12 of ~28 template fields have no reader (43 %) |
| Catalogue duplication | 2 | 6 vendor templates identical but for `entityId`; 7 of 25 never placed |
| Properties panel | 2 | Edits the SHARED asset; cannot reach waves, roster or difficulty |
| Picker | 4 | Search and drag are right; the subtitle shows an inert field |
| On-map feedback | 4 | One ring (`triggerRadius`). `spawnRadius` is never drawn |
| Persistence | 8 | The best part of the system — post-incident, shared mapping, guards |
| Chrome / editor parity | 7 | Undo, pan, zoom, tutorial, Ctrl+S, workspace, ESC entry |
| Spawner runtime FSM | 6 | States correct; a proximity trigger fires once and never re-arms |
| Coupling to the AI | 1 | A spawner can influence exactly two things about what it spawns |
| Encounter difficulty | 7 | `levelBonus` / `scaleWithPlayerLevel` are well designed and have no UI |
| Tests | 7 | ~90 of them; none validates `entityId` or a placement against colliders |
| Observability | 2 | No `spawners` console command, where `ai` / `market` / `journal` exist |
| Authoring a new behaviour | 1 | Impossible from the game. Inspector only |
| Shipped content | 5 | 20 placements, 7 orphan templates, 6 ids out of step with their tile |

## The defects, in the order they matter

### 1. The instance does not exist as a concept

The complete record of a placed spawner:

```json
{ "template_id": "barbol_grove", "zone": "Forest", "tile": [40, 24], "id": "..." }
```

Everything else is resolved through `SpawnerTemplateCatalog.GetById`. There is no field on
the instance that any two placements of one template could disagree about.

`SpawnerInstanceLoader.TryCreateInstance` **reads** an `overrides` block
(`visible_in_game`, `life_defaults`) that `SpawnerEditorManager.SaveInstancesToJson`
**never writes** — and both fields it can carry are themselves inert. Half a feature, dead
at both ends.

### 2. The properties panel edits the shared asset, and says so

`SpawnerEditorManager.UI.cs` closes every committed edit with:

> `"{label} updated on '{template.templateId}' — affects every placed instance of this
> template."`

Six editable rows (`Trigger Radius`, `Cooldown (s)`, `Between Waves (s)`, `Max Active`,
`Restart CD (s)`, `Spawn Radius`), all six writing `SpawnerTemplateData`. Tuning the spawner
you just placed retunes the other two `barbol_grove` in the world.

Two things make it worse than it reads:

- It is a **ScriptableObject edited in Play Mode**, which Unity keeps until the next domain
  reload — so the edit follows the author back into the Editor and rewrites shipped balance.
  The same trap is already recorded against writing a level onto a `MonsterDefinition`.
- The panel does **not** expose the fields that actually define a spawner: `waves`,
  `entityId`, `count`, `spreadRadius`, `levelBonus`, `scaleWithPlayerLevel`, and every enum
  (`Trigger`, `Mode`, `Advance On`, `Spawn Shape` are read-only labels).

`CommitTemplateEdit` is right about the one thing it controls — `SetDirty` alone, never
`Undo.RecordObject`, per the building-template incident.

### 3. The duplication, diffed field by field

The six vendor templates, compared across every serialised field:

```text
shape 1 | spawnRadius 0 | trigger Auto | triggerRadius 10 | autoStart 1 | Periodic
cd 0 | betweenWaves 5 | maxActive 1 | persistent 1 | restartOnDone 1 | restartCD 300
count 1 | spread 0
```

Identical. **The only difference is `entityId`.** Six assets, six ids, six catalogue entries
for one behaviour.

The ten "camp" templates (`barbol_grove`, `barbol_raiders`, `dark_coven`,
`dark_vampire_court`, `red_dragon_lair`, …) share a whole archetype — Burst, `cd 3`,
`betweenWaves 6`, `advanceOn Clear`, `maxActive 0`, `spawnRadius` 12–14 — and differ only in
roster, `restartCooldownSeconds` and the two difficulty knobs.

Seven templates have never been placed, and six of those are iteration fossils whose **name
is the parameter list**:

```text
barbol_auto_pair_3s_max2_restart10
barbol_periodic_no_stack
barbol_defend_fixed10
barbol_defend_random
barbol_pair_10s_restart
barbol_periodic_faster
```

28 % of the catalogue is dead weight, and it is dead weight *because* a knob change costs an
asset.

### 4. A spawner can say nothing about the AI

`MonsterSpawner.SpawnEntity(def, position, persistent, resolvedLevel)`. Four arguments, two
of which are the position and the definition. FSM set, aggro, patrol, standoff, faction and
`desiredRange` all come from the `MonsterDefinition`, shared by every instance of that
monster in the world.

The escape that looks like it exists does not: the FSM resolution order is `by_eid` →
`by_archetype` → `MonsterDefinition.fsmSet`, and `by_eid` is keyed by an **Entities-editor
placement id**, unreachable for anything a spawner creates.

So "this camp patrols, that one ambushes" is not expressible today in any form.

### 5. The editor does not draw the area that matters

`SpawnerEditorManager.Outlines.cs` draws one ring, sized to `triggerRadius`. `spawnRadius` —
the area bodies actually land in, and therefore the area an author must keep clear of walls
— exists only in an `OnDrawGizmosSelected` under `#if UNITY_EDITOR`, which requires selecting
the GameObject in the Hierarchy mid-play.

The 2026-09-07 AI audit measured three shipped placements sitting inside colliders. The
editor has no way to show that.

### 6. Smaller, but real

- **A proximity trigger fires once and forever.** `SpawnerInstance._triggered` is never
  reset, so the authored `proximityInitialOnly` cannot express its own opposite — it is one
  of the twelve inert fields for that reason.
- **The instance id is minted at placement and never re-minted.** It encodes the tile it was
  born on, so moving a spawner desynchronises them: **6 of the 20 shipped records** disagree
  with their own `tile`. Harmless today (nothing parses the id) and a trap the moment
  anything does.

  Two different causes hide in those six, and **the test that separates them is "constant or
  ratio", which is cheaper than either story.** An origin offset is a constant ADDED to both
  axes, so it shows as a fixed shift; Lobby sits at (150, 50) and would be unmistakable.
  Valeria off by one tile on one axis and Gatita by two are rounding. But
  `vendor_roberto_respawn_5m_lobby_21_42` sits at `[11, 20]` — almost exactly **twice** its
  tile on BOTH axes, and a ratio is never an offset. A ratio in this project is a
  pixels-per-unit, and the fossil that names this one was sitting unread at the top of
  `SpawnerInstanceLoader`: `private const float PPU = 32f;`, deleted in this pass. 16 against
  32 is the factor. So those ids were minted by an older build against a different pixel
  scale — a third historical space, not a slip.

  That is the same shape as every Python-pixel sighting this project keeps finding —
  `wallWidth`, the totem's radius, the vortex's radius, `coneLength`, `arcane_flame`'s radius,
  `AuraExecutor`'s divide — where the tell is always a constant compensating for units that
  stopped applying. The next one will look the same, and "is the disagreement a constant or a
  ratio" answers it before any archaeology.

  It is also why the six renames wait for the catalogue cleanup: nothing parses an id, so they
  are inert EVIDENCE, and renaming them before a human has read the migrated file would
  destroy the only record of which rows came from the old space.
- **Zone casing is inconsistent in shipped data** — `lobby` in five records, `Forest`
  capitalised in ten. Zone lookup is `OrdinalIgnoreCase`, so it works; it is still two
  spellings of one zone in a file people read.
- **No `spawners` console command.** `ai`, `journal`, `faces`, `market`, `stats` and
  `grimoire` all exist so their layer is answerable without a UI. This one is not.
- **`UpsertTemplate` has zero runtime callers.** Creating a template means the Unity
  Inspector: right-click → Create → Valkur/Spawner/Template → fill twenty-odd fields → drag
  it into the catalogue asset by hand.

## What is right, and must survive the rework

- **Persistence (8/10).** `SpawnerTileMapping` is shared by save and load, which is the fix
  from `SPAWNER_COORDINATE_SPACE_DRIFT`; there is an anti-wipe guard against writing an empty
  file over a populated one, a 0.75 s autosave debounce, a flush on close and on
  `OnApplicationQuit`, an EditMode write guard against test pollution, and the path is
  map-slot aware. Nothing below should touch this layer except to add the `config` block.
- **Encounter difficulty.** `levelBonus` (PLACE) and `scaleWithPlayerLevel` (PROGRESS) are
  two knobs that are not substitutes, resolved once per wave entry so a pack arrives at one
  level, and stamped on the object via `SpawnLevel` rather than written onto the definition.
- **`kind: "building"` dispatch.** `SpawnerInstance.SpawnBuildingEntry` already routes to
  `BuildingLoader.SpawnAtWorldPosition`, so a fish shoal is harvestable for the same reason a
  tree is. A spawner is already more than "monsters".
- **Editor chrome.** Undo/redo, middle-mouse pan, wheel zoom, drag-from-picker, Alt outlines,
  workspace persistence, General Editor entry. It is at parity with its siblings.

## The design: copy on place

**A template stops being a behaviour and becomes a starting point.**

```text
SpawnerTemplateData    =  PRESET — what a fresh placement is born with
SpawnerInstanceConfig  =  the truth about THIS spawner, in the JSON, schema v2
```

This is `ParticleInstanceConfig` applied to spawners, deliberately and almost verbatim,
because that design has already been argued through in this project and the arguments
transfer unchanged.

### The record

```json
{
  "template_id": "camp",
  "zone": "Forest",
  "tile": [40, 24],
  "id": "camp_Forest_40_24",
  "config": {
    "roster": [
      { "kind": "monster", "entityId": "barbol", "count": 3, "spread": 4 },
      { "kind": "monster", "entityId": "barbol_musgo", "count": 1, "spread": 3 }
    ],
    "trigger": "proximity",
    "triggerRadius": 10,
    "spawnMode": "burst",
    "advanceOn": "clear",
    "restartOnDone": true,
    "restartCooldown": 120,
    "spawnRadius": 12,
    "shape": "circle",
    "levelBonus": 1,
    "scaleWithPlayerLevel": 0.3
  }
}
```

Defaults omitted, exactly as `particles_instances.json` v4 does.

The `template_id` stays on the record. It names **where this configuration came from**, it
drives the picker's grouping and the same-preset outlines, and it is what a "reapply preset"
action reads. It just stops deciding how the spawner behaves.

### Which fields are per-instance and which stay preset-only

| Field | Where it lives | Why |
|---|---|---|
| `waves` / roster | **Instance** | The single most placement-specific thing there is |
| `triggerType`, `triggerRadius`, `autoStart` | **Instance** | Two camps in one clearing want different rings |
| `spawnMode`, `cooldownSeconds`, `betweenWavesCooldownSeconds`, `advanceOn` | **Instance** | Pure pacing; the reason six orphan templates exist |
| `maxActive`, `restartOnDone`, `restartCooldownSeconds` | **Instance** | Ditto |
| `spawnRadius`, `spawnerShape` | **Instance** | Depends on the room it is placed in |
| `levelBonus`, `scaleWithPlayerLevel` | **Instance** | `levelBonus` is PLACE by definition |
| `persistent` | **Instance** | One vendor may be persistent and another not |
| The twelve inert fields | **Neither** | Delete or build, do not snapshot (see below) |

Nothing is preset-only. That is not an oversight: a preset is a *default set*, so every field
it holds must be overridable or the preset is partly a rule. What keeps presets valuable is
that "vendor respawn, 5 min, persistent, max 1" is worth expressing once — not that any part
of it is unreachable.

### The twelve inert fields

`spawnerType`, `randomSpawnRadius`, `defendSpawn`, `defendLeash`, `visibleInGame`,
`proximityInitialOnly`, `damageable`, `maxHp`, `flashOnHit`, `flashColor`,
`flashDurationSeconds`, `hpResetOnEnter`.

They are honestly documented as INERT in their own tooltips, which is better than the usual
shape and is why the axis scores 3 rather than 1. Snapshotting them into every instance
config would multiply twelve dead fields by twenty placements. Each gets one of two verdicts
before v2 ships:

- **Build it.** `proximityInitialOnly` is the cheapest and the most useful: reset
  `_triggered` when the player leaves the radius and a camp can re-arm, which is what a
  wandering-monster region needs. `defendSpawn` / `defendLeash` are a real feature —
  `ChaseState` already has a spawn-anchor leash, driven by the monster rather than by the
  spawner.
- **Delete it.** The "Visual spawner" group (`spawnerType`, `visibleInGame`, and the whole
  Life Defaults block) describes a damageable on-map spawner object that was never built and
  is not on any roadmap. Seven fields, one decision.

### Migration, and the half that fails silently

`SpawnerInstanceLoader` freezes a v1 record against its preset as it loads, folding the
current template values into a `config`, and **writes that back once** (Editor only, the way
`ParticleInstanceSerializer.SerializeRecords` does).

The write-back is not a convenience. In memory alone the freeze lasts one session, so
retuning a preset and restarting would re-snapshot every un-migrated placement from the new
values — the coupling copy-on-place removes, coming back in through the file. The writer must
emit the records it just read, verbatim and complete, including any it could not spawn, with
no scene scan and no coordinate maths; that is what lets it need no anti-wipe guard of its
own.

### What the catalogue collapses to

```text
25 templates   ->   ~6 presets
                    vendor · camp · survival-waves · boss-lair · building-school · blank
                    + 20 per-instance configs
 7 orphans     ->   0        (a knob change no longer costs an asset)
 6 vendor      ->   1 preset + 6 entityId values
   clones
```

## The editor, after the change

One generic **Spawner** tool. Place it, then answer four panels — which is the shape asked
for, and which becomes possible only once the instance owns its data.

| Panel | Edits | Cost |
|---|---|---|
| **Roster** | The wave entries: a picker over `MonsterCatalog` + `BuildingCatalog`, never free text | Medium |
| **Schedule** | trigger, radius, autoStart, cooldowns, `advanceOn`, restart, `maxActive`, `persistent` | Low — all already live |
| **Area** | `spawnRadius`, `shape`, per-entry `spread`, with **both rings drawn** | Low |
| **Difficulty** | `levelBonus`, `scaleWithPlayerLevel` | Trivial — live, and simply absent from the UI |
| **Brain** | FSM set, and later patrol / standoff / faction | **High — not data today** |

Rules the panels inherit from the Particles editor, for the same reasons:

- The properties panel edits **whichever is in scope** — a selected placement, or the preset
  when nothing is placed — and **says which in its first header**. Without that line the
  author cannot tell the two modes apart, and the old bug is one click away from returning.
- The old coupling stays reachable on purpose, through **Reapply preset → This / → All
  placements**. Removing it entirely would make a global retune impossible rather than
  deliberate.

### The Brain panel needs code before it needs UI

There is no field to write. The shape that works is the one `SpawnLevel` already proved: a
component stamped on the GameObject *before* `ConfigureMonster` runs, read by whoever needs
it, absent on every legacy path so nothing else re-tests.

The insertion point is already a parameter —
`FSMRuntimeFactory.TryBuildForEntity(placementId, archetypeKey, fsmSetHint, owner, out fsm)`.
`FSMMonsterBrain` passes `def.fsmSet` as that hint; a `SpawnBrain` component read there in
preference to the definition is a one-line change on the brain side and no change at all to
the factory.

Two constraints:

- **`by_eid` must still win.** It is the Entities editor's per-placement override and is the
  more specific statement. Order becomes `by_eid` → `SpawnBrain` → `by_archetype` →
  `MonsterDefinition.fsmSet`.
- **An unknown set name must refuse loudly**, not fall through. A spawner silently ignoring
  its authored brain is exactly the authored-and-inert shape this whole document is about.

Shipping a Brain dropdown without this makes it the thirteenth inert field.

## Risks

1. **The instance record goes from 4 fields to ~15.** Every guard in the persistence layer —
   anti-wipe, shared mapping, EditMode refusal — was written when the record was trivial.
   `SpawnerFileIntegrityTests` validates zone membership, round-trip and tile collisions and
   will validate nothing about `config`. Extend it in the same change, not after.
2. **Nothing validates `entityId` today.** It is a string, and a typo produces one warning at
   spawn time and a camp that never fills. The roster picker fixes authoring; a shipped-data
   test is what fixes the twenty records already on disk.
3. **A per-instance `persistent` widens a live exemption.** `persistent` exempts every entity
   from `MonsterSpawner`'s despawn sweep. Making it per-placement makes it easy to tick, and a
   world of exempt monsters is a leak with no error.
4. **Presets must not be quietly abolished.** The temptation after copy-on-place is to delete
   the catalogue. Six vendor placements that genuinely share one behaviour are what a preset
   is for; the answer is preset **plus** override.
5. **Creating a preset from the game is still missing** after all of this. It hurts far less
   once a knob change costs nothing, but "save this spawner as a new preset" is the step that
   closes the loop.

## Build order

1. **DONE — Copy on place.** `SpawnerInstanceConfig`, schema v2, loader freeze, Editor
   write-back, `SpawnerInstance` reading its own config. The foundation; until it landed,
   every new panel edited shared data.
2. **DONE — Properties panel on the instance**, with the scope header and the two reapply
   actions.
3. **DONE — Verdict on the twelve inert fields.** `proximityInitialOnly` became a working
   `proximityRearms`; the defend pair became `defendLeashRadius` delivered through
   `SpawnBrain`; the Visual-spawner group and `SpawnerDefinition.cs` are deleted. Done before
   v2 froze, so the schema never carried them.
4. **DONE — Roster panel** with a catalogue-backed picker, plus a shipped-data test over the
   `entityId` strings already on disk, which no picker can reach.
5. **PARTIAL — Second ring done; the collider check is not.** The check must be an
   `OverlapCircle` against World|Building in Play Mode — the collision grid covers terrain
   only, and building colliders are baked at runtime — so it belongs to the editor's placement
   path rather than to the serializer.
6. **DONE — Difficulty rows in the panel.** Two fields, live since encounter difficulty
   shipped and absent from the panel until now.
7. **DONE — `spawners` console command.** Id, preset, zone, state, wave index, live count,
   roster, and the two aggregates worth having unasked: entities currently held, and
   placements carrying the despawn exemption.
8. **DONE — `SpawnBrain` stamping.** `fsmSetOverride` and `defendLeashRadius` reach the panel
   as rows; a set-name dropdown over `sets.json` would be better than a text field and is the
   obvious next increment.
9. **OPEN — Catalogue cleanup.** Once the migrated file has been read by a human: collapse to
   ~6 presets, delete the seven orphans, normalise zone casing, re-mint the six stale ids.

### Tests added

`SpawnerInstanceConfigDefaultsTests` (the default contract, both directions, plus a guard that
the twelve inert fields stay deleted), `SpawnerInstanceSerializerTests` (round trip per field,
wave regrouping, default omission, the freeze, unreadable-vs-empty, unknown enum names, JSON
escaping, and an invariant-culture float check under `es-ES`), `SpawnerCopyOnPlaceTests` (two
placements independent, a placement does not follow its preset, deep-copied roster, orphaned
placement, `ApplyConfig` re-seeding), `SpawnBrainTests`, and `ShippedSpawnerRosterTests` over
the presets and the instances file.

The serializer fixture asserts the COMPOSITION — write then read — rather than either half,
because a serializer and a parser that are each internally consistent and disagree with each
other is exactly the shape of `SPAWNER_COORDINATE_SPACE_DRIFT`.

### Verification

- **Full EditMode suite: 7998 / 7998 passed, 0 failed, 204.6 s.** Structural check clean —
  `completed` equals `total` equals `summary.total`, so it is one pass rather than two crossed
  runners. Console after it holds nothing from `[SpawnerEditor]`, `[SpawnerInstance]`,
  `[SpawnerInstanceLoader]` or `[SpawnerMigration]`.
- **Scoped Spawners run: 150 / 150.**
- **Shipped data migrated:** `Valkur > Spawners > Migrate Instances To v2` reported 20 of 20,
  0 staying v1. Read back off disk: 20 records, 20 configs, 48 roster entries, none empty,
  `survival_10` keeping all 10 waves and 20 entries. Defaults are omitted as designed — the
  vendor rows write no `triggerRadius`, `autoStart` or `betweenWaves`.
- **The new assembly was confirmed LOADED rather than trusted from a green console**, per the
  note about deferred compilation: `SpawnerInstanceConfig.proximityRearms`, `SpawnBrain.Stamp`
  and `SpawnerInstance.Preset` all resolve by reflection and `SpawnerTemplateData.spawnerType`
  is gone.

Five tests went red on the way and all five were FIXTURES asserting the old behaviour, not
production defects. Both causes are worth recognising again:

- Three in `SpawnerInstanceClampToSpawnAreaTests` mutated the preset AFTER `Initialize` and
  expected the live spawner to follow — which is precisely the coupling copy-on-place removes.
  Every measured value was the CONFIG default rather than the unclamped input, so the clamp
  was running correctly against a number the test had never reached. The tell was that the
  actuals were too round to be an accident.
- Two in `SpawnerPersistenceTriggerTests` are source scans that were grepping for strings which
  moved one level down — `FileHasEntries(path)` became `RepositoryHasEntries()`, and
  `MapEditorActiveSlot.DirForActiveSlot` left the writer entirely when it stopped building its
  own path. Same trap `CastOriginContractTests` records: point the fixture at the new owner,
  never re-inline the call to satisfy the grep, which here would have demanded back the exact
  duplicate writer the change exists to remove. The fixture now also asserts
  `File.WriteAllText` is ABSENT — without that it would prove the repository is called and say
  nothing about a hand-rolled writer still sitting beside it.
