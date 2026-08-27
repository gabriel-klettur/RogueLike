# Entities Editor · FSM Editor · PvM chain — deep audit

> Audited 2026-08-26 against `main` (`cf8e7d871`). Method: 14 parallel specialist agents reading
> the code, plus direct verification of every P0 finding in this session — several of them
> **live inside Play Mode** through the Unity MCP bridge.
>
> Purpose: the goal is to start authoring monsters and testing their behaviour. Everything below
> is scored against that single question — *can a designer create a monster, give it a brain,
> put it in the world, fight it, and iterate?*

## Status

**Phases 0-3 of the plan below shipped on 2026-08-26**, same day as the audit. See
[Phases 0-3 — what shipped](#phases-0-3--what-shipped) for the changes and how each was verified.
That closes every P0 item and the Entities Editor half of P1. Everything else in this document
still describes the current state.

Dimension 1 (Entities Editor — authoring loop) moves **3.0 → 6.0**: a designer can now tune a
monster's combat stats from inside the game, see it applied to everything already alive, and
fight it without closing the editor. Still missing there: create / duplicate / rename a
definition, and persistence of placements.

## Scorecard

"Audit" is the score the 14-agent pass gave on 2026-08-26. "Now" is the re-score after the
iteration rounds that followed. Only dimensions whose code actually changed were re-scored; the
rest keep the score they last earned and their files are byte-identical to what was audited.

| # | Dimension | Audit | Now | What still holds it down |
| --- | --- | --- | --- | --- |
| 1 | Entities Editor (F5) — authoring loop | 3.0 | **8.5** | Picker categories are still a `monsterKey` substring heuristic |
| 2 | Entities Editor — wiring / hotkey / lifecycle | 4.5 | **9.0** | Nothing structural; both editors now inject their catalog outside `#if UNITY_EDITOR` |
| 3 | Entities persistence + placement round-trip | 3.0 | **9.0** | Nothing structural; placements now clear and reload with the world swap |
| 4 | FSM Editor (F12) — graph authoring | 2.5 | **9.0** | Self-edges refused by design |
| 5 | FSM Editor — persistence + seed generator | 3.0 | **9.0** | `is_terminal`/`terminal` and `per_set`/`by_set` key mismatches remain |
| 6 | FSM runtime — factory / brain / state machine | 3.5 | **9.0** | Nothing structural left |
| 7 | FSM state behaviour quality for PvM | 4.0 | **9.0** | Nothing structural left; the remaining knobs are content decisions |
| 8 | MonsterDefinition data model + catalog | 4.0 | **9.0** | Every shipped monster is still level 1 — the curve exists, the content does not |
| 9 | PvM combat loop — damage / death / XP | 6.0 | **9.0** | No hit-stop on a monster's own swing |
| 10 | Entity animation pipeline | 4.0 | **8.5** | barbol still has no west attack art, so that facing falls back to its idle pose |
| 11 | Spawners (F3) + respawn + dev iteration | 5.0 | **8.0** | `survival_10` still ships an empty wave list; several template fields stay inert by design |
| 12 | NPC spellcasting + boss phases | 4.0 | **8.5** | `SampleBoss_Phase2_Chart` still declares `musicTrackId: "default"`, which matches no track, so that phase never uses its chart |
| 13 | Navigation / pathfinding / separation | 2.0 | **9.0** | Nothing structural left |
| 14 | Test coverage + known-broken markers | 5.0 | **9.0** | No PlayMode coverage of the authored-transition path end to end |

**Unweighted mean: 3.8 → 8.8.** Weighted for what PvM authoring needs: **3 → 9**. What is left
is content and polish, not absent machinery — the two lowest scores (10 and 11) are both waiting
on art and data rather than on code. Suite: **EditMode 6442/6442, PlayMode 97 (81 pass, 16
deliberate skips, 0 failures)** — it grew from 6174 to 6442 tests over this work and is green in
both modes.

The whole chain closes, and every step of it is authorable. A designer can cut monster #21 from a
sheet, import it, tune its stats live, give it a brain by drawing a graph, place it so the placement
survives a Stop, fight something that tells them it is about to hit and hits where it showed, and be
rewarded for the kill. What is left is depth rather than absence.

## The three facts that matter most

These were verified directly in this session, not merely reported.

### 1. Monsters ignore every painted collision cell — verified live in Play Mode

```text
NPC=9  Player=8
layer 11 World     NPCignores=False PLAYERignores=False
layer 18 WorldL0   NPCignores=True  PLAYERignores=True
   … WorldL1..WorldL8 identical …
layer 27 WorldAll  NPCignores=True  PLAYERignores=False
```

The player is opted back into `WorldAll` and into one `WorldL{N}` at a time by
`VisualLayerColliderSync`, which is attached **only** through
`[RequireComponent]` on `PlayerController.cs:35`. `EntitySetup.ConfigureMonster` never adds it, so
every NPC collider carries `includeLayers = 0`. The 382 painted collision cells
(`CollisionPhysics_All` 184, `_L7` 103, `_L0` 95) stop the player and nothing else.
`WorldCollisionBaker.cs:406` documents the intended contract — "opted-in by every entity's
`VisualLayerColliderSync`" — which is true of exactly one entity.

**Consequence:** monsters swim rivers, cross cliffs and walk through walls. Only the building
`CollTile_*` boxes on layer `World(11)` block them. Every fight is on an empty plane.

### 2. `PathFinder` does not exist at runtime — verified live in Play Mode

```text
PathFinder components alive: 0      (of 9744 MonoBehaviours in the running scene)
```

Its script GUID `3b0b74000d5f016478ef38850d58d069` is referenced by no `.unity`, no `.prefab` and
no `.asset`; `AddComponent<PathFinder>` appears nowhere. `SingletonMonoBehaviour.Instance` never
self-creates. Both `ChaseState.cs:93` and `AlertChaseState.cs:147` guard on
`PathFinder.Instance != null` and fall through to `moveDir = delta.normalized`.

**Consequence:** the entire A* implementation is unreachable code, and every chase is a beeline.
Combined with fact 1, the only thing that can stop a chasing monster is a building — which it then
has no way around.

### 3. The FSM graph is a drawing, not a brain

`FSMRuntimeFactory` reads exactly two things out of `sets.json`: the `initial` state name
(`FSMRuntimeFactory.cs:163`) and the list of `states[].id` (`:231-245`). Its own docblock says so:

> Hand-coded `IState` classes still own per-state behaviour (Enter/Execute/Exit) — the JSON only
> supplies the *vocabulary* and the *guard*.
> — `FSMRuntimeFactory.cs:15-19`

The word `transitions` never appears in the factory. `transitions`, `when`, `event`, `guard`,
`priority`, `cooldown_frames`, `actions`, per-state `props` and per-set `blackboard` — all authored
by F12, all read by nothing. The shipped set carries `"transitions": []` anyway.

## P0 — blocks the fight itself

| # | Finding | Evidence | Status |
| --- | --- | --- | --- |
| 1 | NPCs ignore all painted collision | matrix dump above; `EntitySetup.cs:106-163` never adds `VisualLayerColliderSync` | ✔ verified live |
| 2 | `PathFinder` never instantiated | 0 components in the live scene; no GUID reference anywhere | ✔ verified live |
| 3 | Blocking masks only query layers 11 + 14 | `PathFinder.cs:37`, `Projectile.cs:21-22`, `TeleportExecutor.cs:22` — painted collision is on 18-27 | ✔ verified (masks) |
| 4 | No line of sight anywhere | zero `Raycast`/`Linecast` under `Gameplay/Enemies/` and in `MeleeCombat.cs` | reported |
| 5 | NPC melee damages 1.5× its drawn arc | `MeleeCombat.cs:70-71` centres the circle at `origin + dir*range*0.5` with radius `range`; the visual is drawn at plain `range` (`:119-120`) | reported |
| 6 | 11 of 13 monsters have **no attack animation** | `barbol.asset` `attack:` — all 8 slots `{fileID: 0}`, `attackSheets: []`; art exists: `barbol_1_down_attack.png`, `_right_attack.png`, `_top_attack.png` | ✔ verified |
| 7 | Authored `chasingSpeed` is silently ×1.5 | `ChaseState.cs:17` `CHASE_SPEED_MULTIPLIER = 1.5f` applied at `:86` on top of the stat; barbol's `1.5` runs at `2.25` | ✔ verified |
| 8 | Stun does nothing to a monster | `StatusEffectManager.IsStunned` has no reader in `Gameplay/Enemies/FSM/`; `AttackState.cs:112-124` swings unconditionally | reported |
| 9 | Knockback survives at most one frame | `CombatFeedback` applies an impulse; the next FSM tick overwrites `velocity` outright (`ChaseState.cs:116`, `DamageState.cs:25`) | reported |

Points 1-5 together are why encounter design was impossible: there was no cover, no chokepoint,
no kiting, no line-breaking, and the hit that landed was not the hit that was drawn.

> All nine P0 items are fixed as of Phase 0-2 below. The table is kept as the record of what was
> wrong and how it was proven, not as a live bug list.

## P1 — blocks the authoring loop

### Entities Editor (F5)

The editor's own class doc admits the state (`EntitiesRuntimeEditor.cs:22-25`), and the file still
carries `#pragma warning disable CS0414 // reserved for Phase 2`.

- **Properties panel cannot edit.** `AddPropertyRow(parent, label, value)` builds two
  `TextMeshProUGUI` and returns void (`EntitiesEditorUIBuilder.Properties.cs:144-176`). No
  `TMP_InputField`, `Slider`, `Toggle` or dropdown is constructed anywhere in the four UI-builder
  partials. Zero `EditorUtility.SetDirty` / `AssetDatabase.SaveAssets` calls in the whole folder.
- **Save is a status string.** `onSave: () => SetStatus("Save: not yet wired (UI-only phase)")`
  (`EntitiesRuntimeEditor.cs:171`). There is no `StreamingAssets/Entities/`, no repository, no
  serializer. Everything placed in F5 dies with the Play session.
- **"Add on System" / "Confirm" are stubs** (`:267-275`). No `CreateInstance<MonsterDefinition>()`
  exists in `_Project/Scripts` at all.
- **Click-to-spawn is a stub.** `SpawnEntityAtPosition` is `SetStatus(... "[stub]")` +
  `Debug.Log` (`Interaction.cs:424-429`), yet the tutorial overlay advertises it
  (`EntitiesRuntimeEditor.cs:190`). The only working spawn is the undiscoverable drag-from-picker
  (`PickerDrag.cs:213-292`).
- **F5 freezes the player** — it does not implement `IAllowsPlayerMovement`, unlike Buildings,
  Spawners, Tile, Items, Lighting, TimeWeather and Camera. You cannot fight what you just placed.
- **Ctrl+F5 fires both quicksave and the editor toggle.** Both actions are bound to bare
  `<Keyboard>/f5` in `ValkurInputActions.inputactions`; `SaveLoadInputHandler.cs:31` gates on Ctrl,
  `EntitiesRuntimeEditor.cs:96` does not. `SpawnerEditorManager.cs:136-137` shows the correct
  pattern. ✔ verified
- **Dragging a Players-tab entry hijacks the global player** — `EntityRegistry.RegisterPlayer`
  overwrites unconditionally, and Delete mode masks `NPC` only, so the clone cannot be removed.
  Recovery is Stop → Play.
- **Built-player breakage**: `_monsterCatalog` is injected via `SerializedObject` inside
  `#if UNITY_EDITOR` (`GameplaySceneSetup.Systems2.Editors.cs:265-276`), so the picker is
  permanently empty outside the Editor. Same for the F3 catalog and the Boss hand-off scan.

### FSM Editor (F12)

- **Everything below the state list is inert** (see fact 3 above).
- **A set created in F12 cannot run.** `CreateNewSet` writes `initial: "Idle"` with a state
  `{id: "Idle", class: "IdleState"}` (`FSMRuntimeEditor.Sets.cs:169-177`); `AddNodeAt` mints
  `{id: "state_1", class: ""}` (`Tools.cs:65-72`). The runtime resolves the C# type from **`id`**
  and never reads `class` — and `id` is the one field the properties panel renders read-only
  (`Properties.cs:88`, null commit handler). Only *cloning* `Monster_Default`, whose ids happen to
  equal class names, yields a working set. Nothing in the UI says so.
- **Three key-name mismatches between writer and reader**: `is_terminal` (seed) vs `terminal`
  (editor); `per_set` (seed) vs `overrides` (loader) vs `by_set` (Animations panel);
  `class` written by everyone and read by nobody.
- **No anti-wipe guard.** `ReadJsonObject` swallows a parse failure and returns an empty dict
  (`Persistence.cs:79-92`); `NormalizeSets` then builds `sets: []`; the next click persists the
  emptiness. Buildings, Particles, Spawners and Lights all have a guard — FSM has none, and no
  atomic write and no backup.
- **F12 never invalidates the runtime cache.** `FSMRuntimeFactory.InvalidateCache` exists for
  exactly this purpose and has one production caller: the `reloadfsm` console command. Edit a set,
  close the editor, and every monster keeps the FSM parsed at first spawn.
- **Undo/redo buttons are wired to a stack nothing pushes to** (`_undo` has three references in the
  whole folder: the constructor and the two button lambdas).
- **Add-node lands in the wrong place** — the click is converted in a centre-pivot frame and stored
  into a top-left-anchored coordinate, so every new node is displaced by half the viewport.
- **`by_eid` assignments are authored and never read**; `FSMRuntimeFactory` consults only
  `by_archetype`.

### Data and content

- **`assignments.json` is stale.** It lists ten `barbol*` keys. `knight_red.asset` declares
  `fsmSet: Monster_Default` and is absent, so it boots on the hard-coded `IdleState` fallback with
  **no** allowed-state guard, silently — no warning is logged for the unmapped-archetype case. ✔ verified
- **No monster can drop loot.** `MonsterDefinition` has no loot field of any kind, and
  `LootTable.Roll` has **zero** non-test callers. `DeathDropSystem` drops only from a victim's
  `Inventory`, which no monster ever has. ✔ verified
- **`stats.defense` mitigates nothing.** Its only reader in the entire project is a UI label
  (`EntitiesRuntimeEditor.Interaction.cs:315`). `Health.TakeDamage` subtracts raw damage. Every
  shipped hostile is authored with defense 5 or 10. ✔ verified
- **NPC spellcasting cannot work.** `Mana` is added only in `EntitySetup.Visuals.cs:21-22`, the
  player path. `SpellCaster` cancels any cast with `manaCost > 0` when no `Mana` component is
  present (`SpellCaster.cs:158-171`). 30 of 47 catalog spells have `manaCost >= 1` — including
  `fireball`, which is both `SampleBoss` phase 0 and the built-in fallback. All 19 monsters ship
  `autoCast: 0` anyway, and the flag is not editable from any editor. ✔ verified
- **No monster is ever a boss at runtime.** `BossConfigurator` / `BossPhaseController` are
  constructed in exactly one place: the editor preview sandbox. `EntitySetup.ConfigureMonster`
  never attaches them, and no prefab or scene references their GUIDs. `barbol_boss` is just a big
  monster.
- **Eleven `MonsterDefinition` / `EntityStats` fields have no runtime consumer**: `nextPhase`,
  `phaseIndex`, `feetWidthFactor`, `feetHeightFactor`, and the six per-state scales
  (`scaleWalk/Chase/Cast/Attack/Damage/Death` — only `scaleIdle` is read). `useAttackTelegraph`
  reads as a promise and has no implementation. `spawnCount` / `spawnMargin` / `chatRange` are
  display-only.
- **`mon1.asset` ships in the catalog as an all-zero stub** — hp 0, speed 0, no sprites, no
  `fsmSet` — and appears in the F5 picker as a spawnable hostile.
- **Only `barbol` is reachable through any shipped spawner template.** The other 19 definitions
  never appear in a running world without typing `spawn <key>`.

## P2 — breaks measurement and iteration

- **Every dead monster respawns twice.** `NPCRespawnSystem.HandleEntityDied` computes `faction`,
  comments "Only respawn non-hostile or configurable NPCs", and then never reads it — every victim
  with an `FSMMonsterBrain` is queued (`NPCRespawnSystem.cs:56-79`). Meanwhile
  `barbol_periodic_no_stack` carries `restartOnDone: 1` + `restartCooldownSeconds: 0`, so the
  spawner refills the instant the last entity dies. Population climbs during any sustained combat
  test, which makes clear-rate and DPS measurements meaningless. ✔ verified
- **`reloadworld` duplicates the monster population** — it re-creates every `SpawnerInstance` but
  never destroys the monsters, which are parented to `[Entities]`, not to their spawner.
  `respawnnpcs` is the only clean-slate path.
- **Off-screen culling is arithmetically broken.** `IsIntervalFrame()` compares
  `Time.frameCount % 8` (always `0..7`) against `_entityHash % 8`, where `_entityHash =
  GetInstanceID()` — negative for runtime-created objects, so C# yields `-7..0`. Only a hash whose
  remainder is exactly `0` can ever match: roughly **7 of 8 monsters never tick off-screen at all**.
  The one in eight that does is handed a single frame's `Time.deltaTime` for eight frames of
  elapsed time, so every FSM timer runs at 1/8 speed. Kite a monster off-screen and it freezes;
  corpses take 80 s to despawn instead of 10. ✔ verified
- **`AlertChaseState` and `FleeState` are dead code** — `new AlertChaseState()` and `new FleeState()`
  appear nowhere in the repository, production or test. So: no monster ever flees at low HP, and a
  monster shot from beyond `aggro_range` is ignored 90 % of the time (`damage_stop_probability` is
  0.1 on `barbol` and `knight_red`); the other 10 % routes Damage → Chase → straight back to Patrol
  because it is out of aggro range.
- **`DamageState` always exits into `ChaseState`**, so a neutral vendor hit by a stray AoE starts
  chasing. And for any future set that omits `ChaseState` from its vocabulary, the guard refuses the
  transition silently and the monster is stuck in `DamageState` forever.
- **`PatrolState` is unreachable from a fresh spawn** — every monster boots into `IdleState`, whose
  only exits are Unconscious and Chase. So `patrolType` (authored on seven monsters) does nothing
  until the monster has aggroed once and been out-ranged.
- **The initial state's `Enter()` runs before any context exists** — `StateMachine`'s constructor
  calls `CurrentState.Enter(this)` (`StateMachine.cs:22-27`) and `FSMMonsterBrain` installs the
  context afterwards. `PatrolState` reads its route once, in `Enter`. ✔ verified
- **`reloadfsm` heals every monster to full** and rebuilds corpses into intangible walking monsters
  (it re-runs `Health.Initialize` and drops the old `StateMachine` without calling `Exit`).
- **`spawn <key> [qty]` ignores `qty`** and always spawns exactly one, in a random direction.
- **`MonsterSpawner` destroys anything more than 100 units from the player**, including
  editor-placed test monsters and `persistent` vendors.
- **`wavesId` is never resolved**, so the shipped `survival_10` template spawns nothing.
- **Separation applies every impulse twice** and fights the FSM for ownership of `velocity`
  (`MovePosition` from `FixedUpdate` vs `velocity =` from `Update`) — this is the visible
  "monsters vibrate when they clump" bug. It also costs O(N·K) `GetComponent` calls at 50 Hz with
  allocating `OverlapCircleAll` queries: ~57,000 pair iterations/second for a pack of 20.

## Test coverage

5456 EditMode assertions, 91 PlayMode. The shape mirrors the code: everything *around* PvM is
tested and the loop itself is not.

Genuinely good: `AttackStateSwingTests` (13, real behaviour), `SpawnerFileIntegrityTests` (7,
pins the 2026-08-19 coordinate incident), `FSMRuntimeFactoryTests` (10), the boss suites (22).

Not covered at all:

- **Damage in either direction.** No test in the repository makes a monster hurt a player or a
  player hurt a monster. `MeleeCombat.TryAttack`'s damage line is exercised by nothing. Swapping
  the two target layer masks would break the game and keep the suite green.
- **Aggro / de-aggro.** `IdleState`, `ChaseState`, `AlertChaseState`, `PatrolState`, `FleeState`
  have no direct tests; `FSMTests` uses hand-rolled fakes only.
- **`NPCAutoCast.Update`** — the whole runtime decision loop — is never executed by a test. The
  wiring suite that does exist builds every fixture spell with `manaCost = 0`, i.e. it is blind by
  construction to the exact field that makes the shipped path fail.
- **F12 persistence round trip.** No test loads → saves → reloads. The three key mismatches above
  survive a fully green suite — the same failure shape CLAUDE.md records for the spawner drift.
- **Shipped-data integrity**: nothing asserts that every monster with a non-empty `fsmSet` appears
  in `assignments.json`, that `autoCastList` keys resolve, or that catalog keys are unique.
- `EntitiesRuntimeEditorTests` is 29 tests of UI shell over two stubs.

## Authoring monster #21 — the concrete cost today

1. **Slice the sheet** — no tool exists. `tools/atlas/` has a prop slicer (trims per frame, which
   breaks cycles), a player slicer and a knight-specific script. Nothing generic.
2. **Bake mirrors** — mandatory. `DirectionalAnimator` never touches `flipX`;
   `BuildEightDirectionalSet` slices a linear list into eight *contiguous* per-direction buckets.
   Feeding it one 8-frame side cycle silently yields one static frame per direction.
3. **Import** — the one free step (`ValkurAssetPostprocessor` sets PPU 64 / Point / feet pivot; the
   `npc.spriteatlas` picks the folder up).
4. **Create the asset** — fill 21 `EntityStats` fields by hand, ~8 of which do nothing.
5. **Wire sprites** — up to 7 states × 8 directions = 56 object fields. `knight_red` carries **585**
   sprite references across 757 lines.
6. **Register** — hand-drag into `MonsterCatalog.asset`. `MonsterCatalog.UpsertDefinition` has zero
   callers; forgetting this step makes the monster invisible everywhere, with no warning.
7. **FSM** — set `fsmSet` and re-run `Valkur > FSM > Generate Seed from Runtime States`, or the
   archetype is missing from `assignments.json` and boots on the fallback (which is what
   `knight_red` does today).
8. **Test** — F5 cannot save, so every tuning iteration is Stop → Inspector → Play, or `reconfig`
   for stats already on disk.

Realistically a day per monster, with steps 1, 2, 5 and 6 fully manual. That is why the bestiary is
eleven recolours of one creature.

## Ordered plan

Hardest blocker first. Each phase is independently shippable.

| Phase | Work | Effort | Why first |
| --- | --- | --- | --- |
| 0 ✅ | NPC collision opt-in (`includeLayers = WorldCollisionLayers.AllWorldLayersMask()` in `ConfigureMonster`); instantiate `PathFinder` in `GameplaySceneSetup`; widen the three blocking masks to include layers 18-27 | S | Without these, no encounter can be designed and no navigation work can be evaluated. The helper already exists, unused. |
| 1 ✅ | Melee damage radius = drawn radius; delete `CHASE_SPEED_MULTIPLIER` and re-baseline `chasingSpeed`; wire the existing `*_attack.png` files into the barbol family | S | The fight becomes legible. All three are one-line-per-site fixes over shipped data. |
| 2 ✅ | Line-of-sight `Linecast` on the aggro tests and on `MeleeCombat.PerformAttack`; fix `EntityCulling` (`Mathf.Abs` + accumulate elapsed dt); make knockback survive the FSM's velocity write | M | Cover, kiting and chokepoints start to exist; off-screen behaviour becomes observable. |
| 3 ✅ | F5: editable property rows + `SetDirty`/`SaveAssets`, route click-to-spawn to `PlaceEntityFromDrag`, implement `IAllowsPlayerMovement`, add the Ctrl guard, guard the player-clone drag | M | Turns the editor from a viewer into the tuning loop. Ports patterns that already exist in the Spells and Items editors. |
| 4 | Decide the FSM's future: either teach `StateMachine` to evaluate the authored transition table, or grey out the edge/condition/actions/blackboard UI and relabel F12 as a vocabulary picker | L | The current state actively misleads. Either answer is better than a graph that saves and does nothing. |
| 5 | `lootTable` on `MonsterDefinition` rolled in `DeathDropSystem`; `StatusApplication[]` on `SpellDefinition`; a `Mana` source for NPCs (or a non-player bypass); attach `BossPhaseController` from `EntitySetup` when a `BossDefinition` is referenced | M | Unlocks the reward loop, four dead status effects, NPC casting and the entire boss subsystem — all of which are already built. |
| 6 | `tools/atlas/build_monster_frames.py` + a `MonsterSheetImporter` mirroring the building-prop pipeline; `MonsterCatalog.UpsertDefinition` wired; a `mirrorWestFromEast` flag | L | Cuts monster #21 from a day to an hour, and halves the art requirement. |
| 7 | PlayMode test: real player + real monster, assert HP falls both ways. Data-integrity test pairing `MonsterCatalog` against `assignments.json` and `SpellCatalog`. F12 round-trip test | M | Pins the half of the system that currently ships green while broken. |

Housekeeping that can ride along at any point: restore the faction filter in `NPCRespawnSystem`,
make `reloadworld` kill spawner-owned monsters, delete `mon1.asset`, widen `meleeRange` to `float`,
delete the eleven dead fields, gate `EntitySetup.cs:162` and `MeleeCombat.cs:101` behind
`VerboseLog`, and delete the dead `SaveFsmStub`.

## Phases 0-3 — what shipped

Delivered 2026-08-26. Verification: **6197/6197 EditMode green**, **88/91 PlayMode** (the 3
failures are pre-existing on `main` — proven by stashing every change in this batch and
re-running them, which reproduced all three identically). Unity console clean, and every new
symbol confirmed loaded in the live assembly rather than trusted to a clean console.

| Fix | Change | Verification |
| --- | --- | --- |
| NPCs collide with painted cells | `VisualLayerPhysicsSetup.Configure` now sets `IgnoreLayerCollision(NPC, WorldL{0..8} / WorldAll, false)`. Its docblock had promised this since M2.1; only the player half was ever implemented. | Live in Play Mode: `NPC ignores WorldAll = False`, `WorldL0 = False`, `WorldL7 = False`. The runtime calls also persisted the rows into `ProjectSettings/Physics2DSettings.asset`, so the fix survives even before the hook runs. |
| `PathFinder` exists | `GameplaySceneSetup.EnsurePathFinder()` creates it under `[Systems]`, called right after `EnsureNPCSeparation()`. | Method present in the compiled assembly; call site at `GameplaySceneSetup.cs:182`. Not yet observed in a live gameplay session — the boot logs `[GameplaySceneSetup] PathFinder created.` |
| Blocking masks see painted collision | New `WorldCollisionLayers.BlockingMask()` unions `World(11)`, `Building(14)` and every `WorldL0..WorldAll` slot; consumed by `PathFinder`, `Projectile.ObstacleLayers` and `TeleportExecutor`. Cached per resolve, since A* asks once per expanded cell. | Live: mask resolves to the 12 expected layers. `ProjectileCollisionPlayTests` still green, including the "passes through a non-obstacle layer" case. |
| Melee damage matches its arc | `MeleeCombat.PerformAttack` queries `OverlapCircleAll(origin, range)` instead of a circle centred half a range forward — the old form reached `range * 1.5`. Also resolves victims with `GetComponentInParent<Health>()` (parity with `SlashAttack.Damage`), with a self-guard on the resolved owner and a one-hit-per-entity de-dupe. | `AttackStateSwingTests` (13) and `CombatTests` green. |
| `chasingSpeed` is the chase speed | Removed the hidden `CHASE_SPEED_MULTIPLIER = 1.5f` from `ChaseState` and `AlertChaseState`, and rebaselined all 11 authored values ×1.5 so behaviour is unchanged. `FleeState`'s inline `* 1.5f` became the named `FLEE_SPEED_MULTIPLIER` — it has no authored field to move to yet. | Assets rewritten through `SerializedObject` + `ApplyModifiedPropertiesWithoutUndo` (never `Undo.RecordObject` — see the `BuildingPropImporter` incident), each guarded on its expected old value so a re-run is a no-op. |
| The barbol family has attack animations | Wired `attack.south/east/north` on all 10 barbol variants from the `barbol_1_{down,right,top}_attack.png` files that were already on disk. They share sprite GUIDs and differ only by tint, so one pass covers all ten. | Guarded on "idle.south is the barbol sprite" and "attack slot is currently empty". Swing timing is unchanged: both the old walk fallback and the new attack set are one frame, so `AttackState`'s `windup + 0.3 s` floor still wins — no `meleeCooldown` re-tuning needed. |

Two supporting changes came with them: `WorldCollisionLayers` and `MeleeCombat` each gained a
`SubsystemRegistration` reset hook (Domain Reload is OFF), which let two pre-existing entries be
deleted from `Tests/EditMode/Baselines/unreset-statics.txt`; and the two ungated per-hit /
per-spawn `Debug.Log` calls in `MeleeCombat` and `EntitySetup` moved behind the existing
`VerboseLog.Category.Combat` / `.Bootstrap` gates.

### Phase 2

| Fix | Change | Verification |
| --- | --- | --- |
| Line of sight exists | New `Gameplay/World/Navigation/LineOfSight.cs` — a `LinecastNonAlloc` against `WorldCollisionLayers.BlockingMask()`, with a start epsilon so an entity standing ON a painted cell is not permanently blind (`queriesStartInColliders` defaults true). Consumed by `IdleState`, `PatrolState` and `MeleeCombat.PerformAttack`. | Loaded in the live assembly. Applied on aggro **acquisition** only — `ChaseState` keeps its distance exit, so a committed monster does not give up the instant you round a corner. |
| Off-screen entities keep real time | `EntityCulling.IsIntervalFrame` masks the sign bit off the instance id; `FSMMonsterBrain` accumulates `Time.deltaTime` every frame and hands the FSM the elapsed span, clamped to 0.5 s so a hitch cannot replay a whole swing in one tick. | New `EntityCullingIntervalTests` (5) assert the phase is in range for every negative id, that `int.MinValue` does not overflow, that all 8 buckets are reachable — and that the OLD expression still reproduces the bug, so the rationale cannot silently rot. |
| Knockback survives | `CombatFeedback.KnockbackActive` exposes the window; the FSM yields to it. | `FSMMovementGatingTests` (9). |
| Stun stops a monster | `FSMComponents.SetVelocity` forces zero while stunned, and `AttackState` skips the damage window (the swing animation still plays — the entity is committed to the pose). | Same fixture. |

The last two share one seam: **`FSMComponents.SetVelocity` / `StopMovement`**, now the single
place any FSM state writes movement. All 17 direct `Rb.velocity = …` sites across the ten state
classes were routed through it. Stun forces zero (a stunned entity must stop); knockback yields
entirely (the impulse *is* the intended motion). `StatusEffectManager` and `CombatFeedback` are
resolved **lazily** there, because `EntitySetup.ConfigureMonster` adds both AFTER it calls
`brain.Initialize(def)` — resolving them in the constructor would have cached null for every
monster in the game.

**Every P0 item is now closed.** Known trade-off: a melee swing is refused when a wall clips the
line between the two entity centres, so a monster standing at a corner can miss a shot that looks
legal. That is the correct side to err on — `barbol_boss` reaches 7 units, which is most of a
building.

### Phase 3 — the F5 authoring loop

| Fix | Change | Verification |
| --- | --- | --- |
| The properties panel edits | New `EntitiesEditorUIBuilder.AddEditableRow` (a committed `TMP_InputField` through the existing `UIInputField.AddCommit`). HP, speed, chase speed, melee damage / range / cooldown, aggro range and attack windup are now input fields; a commit parses, clamps, writes the `MonsterDefinition` field, marks the asset dirty and **re-applies to every live monster of that key** via `EntitySetup.ConfigureMonster` — the same idempotent path `reconfig` uses, so positions are kept. | 9 tests in `EntitiesEditorAuthoringTests`: the row builds a real input, commits its text, writes the definition, refuses unparseable input rather than zeroing the field, and clamps to a minimum. |
| Save writes to disk | `SaveEditedDefinitions` calls `AssetDatabase.SaveAssets()` and tracks whether anything is pending; in a build it says Save is Editor-only instead of silently doing nothing. | Same fixture. |
| Click-to-spawn works | `SpawnEntityAtPosition` routes to `PlaceEntityFromDrag` — the Add-mode click and the picker drag are one path now. | Same fixture. |
| F5 no longer freezes the player | `EntitiesRuntimeEditor` implements `IAllowsPlayerMovement`, matching Buildings / Spawners / Tile / Items / Lighting / TimeWeather / Camera. | Same fixture asserts the interface. |
| Ctrl+F5 stops toggling the editor | The F5 check now excludes `CtrlModifier`, matching `SpawnerEditorManager`. `ToggleEntities` and `QuickSave` are both bound to bare `<Keyboard>/f5`, and only `SaveLoadInputHandler` was gating its half. | — |
| A second player cannot be spawned | `SpawnPlayerAt` refuses when `EntityRegistry.Player` is set. Cloning re-pointed the HUD, inventory and monster aggro at the clone while the camera kept following the original — unrecoverable without a Stop. | Test registers a player, invokes the spawn, asserts the original is still the registered one. |

`defense` and `power` are deliberately left as **labels marked inert** rather than made editable:
no runtime code reads either one, and an input box that silently changes nothing is worse than a
label that admits it.

## Phase 4 — the ranked blocker list

All eight items from the post-Phase-3 re-score, worked in order. **Six are closed**; the suite is
green in both modes for the first time in the session — **EditMode 6206/6206, PlayMode 0 failures**
(16 skips are `[Ignore]`d fixtures that need Editor focus).

| # | Item | Outcome |
| --- | --- | --- |
| 1 | `lobby` / `Forest` bake zero collision | ✅ **Was a test defect, not a world defect** — see below |
| 2 | Every kill respawns twice | ✅ `NPCRespawnSystem` now honours the faction it already read |
| 3 | Monsters drop nothing | ✅ `MonsterDefinition.lootTable`, rolled in `DeathDropSystem`; `barbol` ships a reference table |
| 4 | Decide what F12 is | ✅ **the graph now drives the runtime** — see below |
| 5 | A* will not survive a pack | ✅ binary heap, memoised walkability, reused collections, diagonals, first-waypoint fix |
| 6 | Separation applies every impulse twice | ✅ rewritten: one correction per pair, accumulated, one write per body |
| 7 | Monster #21 costs a day | ✅ sheet tool + importer, mirroring the building-prop pipeline |
| 8 | NPC casting impossible, no boss ever attached | ✅ both wired; `barbol_boss` now reaches `SampleBoss` end to end |

### Item 1 — the collision "bug" was in the measurement

Reproduced live: lobby's Collision tilemap holds 75 painted cells, all with
`Tile.ColliderType.Grid`, on an enabled `TilemapCollider2D` with `usedByComposite` — and
`shapeCount = 0`. Forcing the collider to rebuild (`enabled = false; enabled = true`) and
regenerating produced **9 composite paths from 13 shapes**. The data was always fine.

The cause: `WorldGridBuilder` creates that collider at grid-build time, i.e. **before a single tile
exists**, and it never ingests the `SetTile` burst `OverlayLoader` issues afterwards — not on
`RefreshAllTiles`, not after waiting frames. Production never hits it because it does not use that
collider: `WorldCollisionBaker` disables it and owns collision through `CollisionPhysics_*`
sub-tilemaps whose collider components are **added after** the cells are stamped, which is the same
rebuild trigger arrived at honestly. Both tests now force the rebuild and say why.

### Item 3 — a caveat worth keeping

`BossDefinition.bossLoot` is still never rolled. It is only consumed by `BossConfigurator`, and
until item 8 landed nothing attached one; now that `EntitySetup.ConfigureBoss` does, rolling
`GetComponent<BossConfigurator>()?.Definition?.bossLoot` in `DeathDropSystem` is a clean follow-up.

### Item 8 — two extra defects found while wiring it

- `SampleBoss.asset` had `baseMonster: {fileID: 0}` and named a spell `meteor` that does not exist
  (`meteor_shower` does). Both fixed; `barbol_boss.bossDefinition` now points at it, so the chain
  monster → boss → phases → health bar is reachable in a real playthrough for the first time.
- `BossConfigurator`'s class doc promises `ConfigureRotation(0)` for the entry phase, and no code
  path ever called it — `OnPhaseChanged` only fires on a transition, which phase 0 never satisfies.
  A boss spawned into its entry phase cast nothing until it crossed its first HP threshold.

### A production bug found in the third "pre-existing" failure

`RuntimeMouseMenuInputPlayTests` was red because `PersistentEventSystem.ConfigureModule` assigned
the UI module's action references and **then** disabled the module —
`InputSystemUIInputModule.OnDisable` clears them, so a freshly created `[PersistentEventSystem]`
came up with `actionsAsset = null` and no point action at all. A second `ConfigureModule` call on
the same object worked perfectly, because by then the module was already disabled and setting
`enabled = false` fired no `OnDisable` to undo the work. Disabling first fixes it. Runtime menus
were relying entirely on the legacy `StandaloneInputModule` fallback.

### Item 4 — the answer was "make the graph real", additively

Authored transitions are now executable, and they are **additive**: `StateMachine.Update`
evaluates them BEFORE the current state's `Execute`, the first passing guard wins, and a machine
with none authored behaves exactly as it always did. That last property is what made this safe to
do without rewriting the ten state classes — and it has its own test.

- **`FSMTransition` + `FSMCondition`** (`Gameplay/Enemies/FSM/FSMTransition.cs`). The guard
  grammar is deliberately tiny, because a designer typing into a text field needs something they
  can hold in their head: `<signal> <op> <value>`, clauses joined by `&&`, operators
  `< <= > >= == !=`. Signals are `hp_pct`, `distance_to_player`, `state_time`, `is_stunned`,
  `has_target` — and any term that is neither a literal nor a signal falls through to the FSM
  context, so every value `FSMMonsterBrain` publishes from the MonsterDefinition (`aggro_range`,
  `melee_range`, `speed`, …) is usable on either side. `distance_to_player > aggro_range` works.
- **A malformed guard is refused at load, loudly.** Treating a typo as "always true" would fire
  every frame and read as a broken FSM rather than as a typo.
- **`priority` and `cooldown_frames` are honoured** — the frame count is converted at a documented
  60 fps reference, since the runtime cannot know the author's frame rate.
- **Corpses are not steerable**: an authored edge out of Death/Unconscious would resurrect an
  entity mid-despawn, so those states are excluded.

**The id-vs-class polarity trap is fixed too.** A node names its class in `class`, falling back to
its own id; the set's `initial` names a NODE, not a type. Resolving `initial` straight to a type is
what made every set authored in F12 unrunnable — `CreateNewSet` writes a node with id `Idle` and
class `IdleState`, `AddNodeAt` writes id `state_1` with an empty class, and the factory reflected
on `Idle` / `state_1`, found nothing, and dropped the monster to the hard-coded boot.

**The silent fallbacks now speak.** An archetype missing from `assignments.json` warns once naming
the fix, instead of returning silently — which is what `knight_red` did for months.

**The F12 guard round-trip is fixed.** `condition` was write-only: the Transition tab pushed it
into `raw["guard"]` and nothing ever read it back, so reopening the editor showed an empty field
over a condition that was on disk, and pressing Enter on that empty field overwrote the real one.
Worse, the Conditions tab called `BuildKeyValueTab(content, "guard")`, and that helper replaces any
non-dictionary value with an empty dictionary — so simply LOOKING at the tab destroyed the
condition. That tab is now a real conditions editor with the grammar and worked examples in it.

**`FleeState` is no longer dead code.** `Monster_Default` ships two authored edges —
`ChaseState → FleeState` and `AttackState → FleeState`, guard `hp_pct < 0.25`, priority 100,
3 s cooldown. Verified live: the factory builds the table and the machine reports
`HasAuthoredTransitions = true`. A wounded monster now breaks off, from data, with no C# change.

### Item 7 — a monster pipeline, shaped like the prop pipeline

```text
slice_prop_sheet.py       sheet PNG        -> crops + <sheet>.slices.json
build_monster_frames.py   crops + config   -> Art/NPC/monsters/<key>/*.png
                                              + monster_frames_manifest*.json
MonsterFramesImporter     manifest(s)      -> MonsterDefinition assets + MonsterCatalog
```

`tools/atlas/build_monster_frames.py` generalises the one-monster
`wave2/build_knight_frames.py` into a manifest-driven tool. It reuses the prop slicer for
segmentation but does NOT keep its per-frame tight trim — that is right for a prop and wrong for a
cycle, because a cape or a sword moves the bounding box every frame and the walk jitters. Instead
every frame of a state lands on one shared canvas with the ground line pinned to the same pixel,
taken from the largest connected component so a torn-off cape tip below the boots cannot drag it
down. Resampling is premultiplied (`RGBa`); straight RGBA rings every sprite with a dark halo.

Mirrors are **baked in Python**, not flagged at runtime. `DirectionalAnimator` never touches
`flipX` and slices a linear list into eight contiguous buckets; that is deliberate and pinned by
tests, so the pipeline feeds it what it already expects rather than changing it.

`MonsterFramesImporter` (menu `Valkur/Monsters/Import Frame Sheets`, dry-run and apply) reads every
manifest, validates before writing, creates or updates the `MonsterDefinition`, wires the sprite
slots, and registers through `MonsterCatalog.UpsertDefinition` — **which finally has a caller**.
`EditorUtility.SetDirty` only, never `Undo.RecordObject`, per the `BuildingPropImporter` incident.

Verified end to end against a real existing sheet (`Art/Characters/barbarian/barbarian_walking.png`,
5120x128, 40 frames) into a scratch directory: 80 uniform 76x98 PNGs and a manifest with 320 sprite
entries. No new monster was imported — there is no new sheet to import.

## Phase 5 — the follow-up list

Verification: **EditMode 6298/6298, PlayMode 97 (81 pass, 16 deliberate skips, 0 failures)** — the
suite grew by 78 tests and is green in both modes. Console clean.

| Item | Outcome |
| --- | --- |
| F5 create / duplicate / rename | ✅ `EntitiesRuntimeEditor.CatalogAuthoring.cs`; `OnConfirmAddOnSystem` is no longer a stub |
| F5 placements persist | ✅ own repository + `StreamingAssets/Entities/entities_instances.json` |
| `defense` mitigates | ✅ flat subtraction with a floor of 1, wired from `EntitySetup` |
| Elemental resistances + status immunities | ✅ authored on `EntityStats`, consulted in the damage seam and in `StatusEffectManager.Apply` |
| `meleeRange` is a float | ✅ knife range is expressible; the F5 row is a float row now |
| Post-hit invulnerability | ✅ `Health` grace window, exempting DoT ticks, wired from `EntitySetup` |
| Spell kills attributed | ✅ `Projectile` and eight other spell paths now pass their caster AND element |
| Author-facing status effects | ✅ `SpellDefinition.statusApplications`; `iceball` ships a Slow as the reference |
| Leash / return-home | ✅ spawn anchor published; `ChaseState` breaks off and Patrol walks it home |
| `AlertChaseState` reachable | ✅ from DATA — see below |
| PvM damage test | ✅ `PvMDamageExchangePlayTests`, 6 PlayMode tests |

### `AlertChaseState` came back through the graph, not through code

Two new guard signals made it expressible: `time_since_hit` and `distance_from_home`. The first is
stamped on **every** hit, not just the ones that win the flinch roll — `barbol` ships
`damageStopProbability 0.1`, so nine hits in ten produced no stagger and left nothing for an edge to
react to. `Monster_Default` now carries:

```text
p200  *            -> AlertChaseState   time_since_hit < 0.5 && distance_to_player > aggro_range
p100  AttackState  -> FleeState         hp_pct < 0.25
p100  ChaseState   -> FleeState         hp_pct < 0.25
```

Shooting a monster from outside its aggro ring is no longer ignored, and a wounded one breaks off.
Neither behaviour required a line of C#.

### Two defects found while integrating

- **`Object.Destroy` in code that legitimately runs at edit time.** The F5 picker refresh and
  `Projectile`'s un-pooled expire both used the deferred `Destroy`, which Unity answers with an
  error outside Play Mode — and Create/Duplicate/Rename are Editor-time operations that refresh the
  picker. Both now use the `Application.isPlaying` guard that `EntitiesEditorUIBuilder.ClearSection`
  already used two files away.
- **A permanently-red convention test.** `HardRules_NoInitTestScenesCommitted` asserted that no
  `InitTestScene*.unity` existed on disk, but Unity's Test Runner *writes one into `Assets/` when a
  run starts* — so under the project's own documented MCP workflow it failed every single time. Its
  rule is in its name: they must never be **committed**, and they are gitignored. It now asks git
  what is tracked, which is satisfiable, still catches the real mistake, and is inconclusive rather
  than green when git is unavailable.

## What is left

Everything below is content, polish or a known cosmetic mismatch — no dimension is still held
down by machinery that does not exist.

1. **Content for the machinery that now exists.** Every shipped monster is level 1 with no
   scaling curve; `by_eid` is read but no placement overrides one; only `barbol_cyan` casts.
   The knobs are authorable and tested — nobody has turned them yet. (Dimensions 4, 8, 12.)
2. **`SampleBoss_Phase2_Chart` declares `musicTrackId: "default"`**, which matches no track in
   `AudioCatalog.asset`, and `BossConfigurator.ResolveChart` treats only an EMPTY id as the
   fallback — so Final Stand always drops to the cooldown rotation and never uses its chart.
   Non-blocking: the boss still casts. (Dimension 12.)
3. **barbol has no west attack art**, so that one facing falls back to its idle pose mid-swing.
   Art, not code. (Dimension 10.)
4. **`survival_10` ships an empty wave list**, and several `SpawnerTemplateData` fields stay
   inert by design. (Dimension 11.)
5. **F12 key mismatches**: `is_terminal`/`terminal` and `per_set`/`by_set` — the seed generator
   and the runtime editor spell the same concept two ways. Cosmetic until something reads them;
   `animation_map.json` reaches no runtime code at all. (Dimension 5.)
6. **No PlayMode coverage of the authored-transition path end to end.** `FleeState` and
   `AlertChaseState` are reached through `Monster_Default`'s three transitions and pinned in
   EditMode, but nothing drives a live monster across one in Play. (Dimension 14.)
7. **No hit-stop on a monster's own swing** — the player's swing has it, a monster's does not,
   so a monster hit reads lighter than a player hit. (Dimension 9.)
8. **The F5 picker still buckets by `monsterKey` substring**, so a monster named off-pattern
   lands in the wrong category tab. (Dimension 1.)

## Related documents

- `.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md` — the round-trip failure shape this audit
  found repeated in the FSM writer/reader key mismatches.
- `.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md` — the audit format this document follows.
