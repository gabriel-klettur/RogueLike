# Hostile entity AI — audit

> Audited 2026-09-06 against `Scripts/Gameplay/Enemies/`, `Scripts/Gameplay/World/Navigation/`,
> `StreamingAssets/FSM/*.json` and the 19 shipped `MonsterDefinition` assets.
> Every number below was read out of the shipped source or data, not estimated.

> **Status 2026-09-06 (same day): implemented.** Every item in "Suggested order of work" below
> was built and is green — 340/340 across `Game.AI`, `Editors.FSM`, `PathFinderTests`,
> `LineOfSightTests` and `Game.Enemies`, inside a 7743-test EditMode run. The audit body is
> left exactly as written; what changed is recorded in **What was done** at the end, with the
> re-scored table beside the original.

**Overall: 5.2 / 10.** The chassis is good and the content is not. The FSM layer, the
navigation layer and the failure handling are professional work; what runs on top of them is
eleven monsters with one behaviour, three authored edges, and a designer surface that no
shipped asset uses.

## Scores

| # | Dimension | Score | One-line verdict |
|---|---|---|---|
| 1 | FSM architecture & extensibility | 7.5 | Clean, guarded, documented — but two machines (authored + coded) and only one is drawn |
| 2 | Perception / senses | 4.0 | Line of sight exists; no FOV, no memory, no hearing, no search |
| 3 | Decision making & tactical variety | 3.0 | Approach and swing. No standoff, no kiting, no repositioning, no retreat that works |
| 4 | Navigation & pathfinding | 7.0 | 8-way A\*, string-pulled, memoised, GC-conscious. No budget across frames |
| 5 | Melee combat behaviour | 6.5 | Windup, telegraph, weighted moveset, stun honoured — used by 1 of 11 monsters |
| 6 | Ranged / caster AI | 3.0 | Casters close to melee range; distance gates exist with nothing to satisfy them |
| 7 | Group coordination | 0.0 | Does not exist. No aggro sharing, no threat, no reinforcement, no formations |
| 8 | Data-driven authoring | 6.0 | Editor + guard grammar + tuning struct shipped; almost nothing is authored through them |
| 9 | Monster data quality | 3.0 | One entity is all zeros, one patrols 4x faster than it chases, nine are clones |
| 10 | Bosses | 5.0 | Phase controller + cues + audio are real; one boss, no adds, no phase-specific behaviour |
| 11 | Factions & allies | 6.0 | `FactionTargeting` is the right seam; `stats.faction` still reaches no decision |
| 12 | Performance & scalability | 6.5 | Culling, caches, hoisted buffers — offset by per-frame `GetComponent` and per-repath allocs |
| 13 | Robustness / failure handling | 7.5 | Refusals warn, casts cap, catch-up clamps, corpses tear down. Best-scoring axis |
| 14 | Observability / debugging | 5.0 | State name in two HUDs and the FSM editor. No gizmos for aggro, path, LOS or target |
| 15 | Test coverage | 6.0 | 16 AI fixtures, ~360 asserts — and zero over PathFinder, LineOfSight or separation |

## What is genuinely good

- **`FSMComponents.SetVelocity` is a single movement seam.** Stun forces zero, root forces
  zero, knockback yields entirely. Without it the states overwrite every impulse in the game.
- **`FSMTuning` owns every feel knob**, its key AND its default, and publishes only what the
  author set — so an unset field cannot zero a monster's hysteresis.
- **`LineOfSight` is one implementation** shared by aggro acquisition, NPC melee and the A\*
  string-pulling pass, with a start-epsilon so an entity standing on painted collision is not
  permanently blind.
- **`PathFinder`**: octile heuristic, no corner cutting, binary min-heap (was an O(n)
  `SortedList`), memoised walkability invalidated by the collision baker, start cell dropped,
  path smoothed.
- **`FSMMonsterBrain` accumulates `deltaTime` every frame** and clamps the catch-up, so an
  offscreen monster's windup and despawn timers do not run at an eighth speed.
- **`StateMachine` warns once per refused `From>To`.** A deleted node used to be a silent
  deadlock.
- **`FactionTargeting`** replaced twenty `EntityRegistry.Player` call sites with one question,
  which is what made allied summons possible without forking the state classes.

## Findings, worst first

### A. Data defects that are live right now

1. **`mon1.asset` is entirely zero** — `hp: 0`, `speed: 0`, `aggroRange: 0`, `meleeDamage: 0` —
   and `assignments.json` maps it to `Monster_Default`. It spawns dead-on-arrival and cannot
   perceive anything.
2. **`barbol_oscuro` has `speed: 10` against `chasingSpeed: 2.25`.** It patrols four times
   faster than it chases, and `FleeState` runs at `speed x 1.5` = **15 u/s** — roughly half a
   screen width per second.
3. **Nine of eleven hostiles are identical**: `meleeRange 3`, `meleeDamage 5`, `aggroRange 10`,
   `chasingSpeed 2.25`. The colour is the only difference.
4. **`aiTuning` is authored on nothing.** The block does not even serialise into the shipped
   YAML, so all eight knobs run on `FSMTuning`'s defaults for every monster in the game.
5. **Telegraph is nearly unreachable.** `AttackState.MinWindupToTelegraph` is 0.15 s and nine of
   eleven hostiles author `attackWindupSeconds: 0`, so `useAttackTelegraph` can only ever fire
   on `barbol` (0.5) and `knight_red` (0.45) — and it is authored on exactly those two.
6. **`attackVariants` is populated on `knight_red` alone.** Every other monster takes the
   uniform-random branch over whatever the animator holds.

### B. Behaviour gaps

1. **No group coordination of any kind.** Grep returns nothing for aggro sharing, threat,
   reinforcement or squads. Twenty monsters converging are twenty independent monsters that
   happen to agree.
2. **Casters have no standoff.** `NPCAutoCast` has `minDistance` / `maxDistance` per entry, but
   `ChaseState` closes to `melee_range` unconditionally, so a caster walks into the player's
   face and casts from there. Nothing implements a desired range.
3. **Perception has no memory and no cone.** Sight is checked on ACQUISITION only and it is
   360 degrees — a monster is never approached from behind. Once committed, chase never breaks
   on losing sight, and there is no "last known position" to search.
4. **Flee is a straight line into a wall.** `FleeState` normalises away from the target with no
   pathfinding and no LOS test. It then returns to `PatrolState`, which re-aggros on the next
   tick, so at HP below 25 % a monster oscillates on the 3 s transition cooldown.
5. **The authored half of the FSM is nearly empty.** Three transitions per set, and
   `Actions` / `Blackboard` / per-state `props` round-trip to disk and reach no runtime code.
6. **No difficulty scaling beyond `level`**, which touches only hp / meleeDamage / defense.

### C. Engineering debt

1. **No frame budget on pathfinding.** 20 chasers repath every 0.5 s at up to `maxNodes: 2000`
   expansions each, all in the same frame if their timers align. Nothing time-slices.
2. **Per-frame `GetComponent` in the hot path.** `ChaseState`, `PatrolState`, `AlertChaseState`
   and `AttackState` each resolve `Health` and `PlayerSpiritState` off the target every tick,
   and `AttackState` calls `FactionTargeting.EnemyOf` up to four times per frame.
3. **Allocations per repath and per transition** — `FindPath` returns a fresh `List<Vector2>`,
   and every transition is a `new XState()`.
4. **`PathFinder.maxPathLength` is dead** (`#pragma warning disable CS0414` around it), and
   `_walkCache` grows unbounded for the session.
5. **Untested layers**: `PathFinder`, `LineOfSight`, `NPCSeparationSystem`, the chase leash and
   the LOS aggro gate have no fixture at all.
6. **No AI visualisation.** `DebugHUD` and `TargetHUD` print the state name; nothing draws an
   aggro radius, the live path, the LOS ray, the leash anchor or the current target.

## Suggested order of work

1. Fix the six data defects in section A — hours of work, and it moves items 5, 9 and part of 3.
2. Give `ChaseState` a **desired range** read from context (0 = melee). One field turns every
   caster and every future archer into a distinct fight.
3. **Perception memory**: last-known-position plus a search state, and an FOV cone with a rear
   arc that only reacts to being hit. Both are additive to `IdleState` / `PatrolState`.
4. **Aggro sharing** — one broadcast on acquisition over `EntityRegistry.Monsters` within a
   radius. Cheapest single thing that makes a pack read as a pack.
5. Make `FleeState` path, and give the flee edge a hysteresis so it cannot oscillate.
6. A **frame budget** on repaths (a global token bucket in `PathFinder`), and hoist the
   per-frame `GetComponent` calls into cached target resolution.
7. Fixtures for `PathFinder` / `LineOfSight` / leash / LOS aggro, and AI gizmos behind the
   existing `VerboseLog` category pattern.

## What was done (2026-09-06)

### Re-scored

| # | Dimension | Was | Now | What moved it |
|---|---|---|---|---|
| 1 | FSM architecture & extensibility | 7.5 | 8.0 | `SearchState`, `IsStateAllowed`, one shared path follower instead of three copies |
| 2 | Perception / senses | 4.0 | 7.5 | `FSMPerception`: one acquisition rule, a field of view, sight memory, last-known-position |
| 3 | Decision making & tactical variety | 3.0 | 6.0 | Standoff band, working retreat, search, alert pickup. Still no threat model |
| 4 | Navigation & pathfinding | 7.0 | 8.0 | Frame budget, non-allocating `TryFindPath`, `maxPathLength` given a reader, first tests |
| 5 | Melee combat behaviour | 6.5 | 7.0 | Telegraph now reachable on six monsters instead of two; target resolved once per frame |
| 6 | Ranged / caster AI | 3.0 | 6.5 | `desired_range` standoff; `barbol_cyan` authored at 7 and its panic-kite edges retired |
| 7 | Group coordination | 0.0 | 5.0 | `AggroBroadcast`. Shouting only — no threat table, no roles, no flanking |
| 8 | Data-driven authoring | 6.0 | 7.5 | Six new `aiTuning` knobs, authored on all twelve hostiles |
| 9 | Monster data quality | 3.0 | 7.0 | Five of six defects fixed; eleven clones became eleven behaviours |
| 10 | Bosses | 5.0 | 5.5 | Boss and colossus authored (long leash, long memory, wide shout). Still one boss, no adds |
| 11 | Factions & allies | 6.0 | 6.5 | The shout refuses to cross sides; `stats.faction` still reaches no decision |
| 12 | Performance & scalability | 6.5 | 8.0 | Path budget, reused waypoint lists, per-frame target cache |
| 13 | Robustness / failure handling | 7.5 | 8.0 | Flee can no longer corner itself or oscillate; cornered caster fights |
| 14 | Observability / debugging | 5.0 | 7.5 | `ai` console command and the `ai on` overlay |
| 15 | Test coverage | 6.0 | 8.0 | 5 new fixtures, ~50 tests over the layers that had none |

**Overall: 5.2 → 7.1.**

### The seven items

1. **Data defects.** `mon1` given playable stats (was all zeros while mapped to
   `Monster_Default`); `barbol_oscuro` fixed (`speed 10` against `chasingSpeed 2.25`, and
   `speed` is what `FleeState` multiplies); the nine clones differentiated on chase speed,
   aggro, windup, view cone and shout radius; `aiTuning` authored on all twelve hostiles (the
   block had never been serialised at all); telegraph now authored where the windup can
   actually carry it — `barbol`, `barbol_gris`, `barbol_musgo`, `barbol_gigante`,
   `barbol_boss`, `knight_red`, `mon1`. `ShippedMonsterDataSanityTests` pins the structural
   rules so none of it can come back. **Not fixed:** `attackVariants` still exists only on
   `knight_red`, because a moveset needs art nobody has drawn.
2. **Standoff.** `desired_range` on `AIBehaviourTuning`, read by `ChaseState`: advance outside
   the band, hold inside it, give ground below it, and swing anyway when cornered. Zero (every
   melee monster) is the historical behaviour exactly.
3. **Perception memory.** `FSMPerception` is now the single acquisition rule for `IdleState`,
   `PatrolState` and `SearchState`. Adds `fov_degrees` (default 360, suspended for two seconds
   after any hit so a monster stabbed in the back can still turn round), `FSMTargetMemory`, and
   `SearchState` — walk to the last sighting, look around, give up.
4. **Aggro sharing.** `AggroBroadcast` writes an alert into nearby machines of the same side;
   the listener acts on its own tick, and is skipped entirely if its set has no
   `AlertChaseState` — which is what keeps vendors out of it.
5. **Flee.** `FSMRetreat` probes a nine-heading fan with the same `LineOfSight` everything else
   uses, so a cornered monster stops instead of grinding into a wall; the exit sets a regroup
   window (`FSMPerception.SuppressAggro`) that ends the flee/chase oscillation.
6. **Performance.** `PathFinder.TryFindPath` is non-allocating and budgeted
   (`maxSearchesPerFrame`, default 4) with "refused this frame" distinguishable from "no path";
   `FSMComponents` resolves the target, its `Health` and its `PlayerSpiritState` once per frame
   instead of up to four times per frame in `AttackState` alone.
7. **Tests and tooling.** `LineOfSightTests`, `PathFinderTests`, `PerceptionTests`,
   `ChaseBehaviourTests`, `FleeAndSearchTests`, `AggroBroadcastTests`,
   `ShippedMonsterDataSanityTests`. Plus `ai` / `ai on` in the DevConsole: a per-monster report
   (state, target, distance, sight, knobs) and a `LineRenderer` overlay drawing the aggro ring,
   the leash ring around HOME, the view cone and the line to the current target.

### Two traps worth keeping

- **`FSMBuiltInTransitionRegistryTests` was right to go red.** It asserted that `FleeState` and
  `AlertChaseState` were reachable ONLY from authored data, and the shout deliberately makes
  the second one reachable from code. The fix is not to weaken the test: it is now two tests,
  one keeping the Flee guarantee unchanged and one asserting `AlertChaseState` is entered from
  EXACTLY `IdleState` and `PatrolState`. A third entry point is a new way into a state that
  ignores the aggro ring, and that should have to be a decision.
- **A test harness can zero the thing it is measuring.** Both field-of-view tests set
  `Rigidbody2D.velocity` as the monster's facing BEFORE `fsm.Begin()` — and entering
  `IdleState` calls `StopMovement`, so the facing was erased and both tests measured the
  default east facing. They reported a working cone as broken; the same shape would have
  reported a broken cone as working.

### Still open

Threat/aggro tables, roles and flanking (the shout is the whole of coordination); patrol paths
still ignore geometry; `stats.faction` still reaches no decision; boss adds and phase-specific
behaviour; difficulty scaling beyond `level`; `attackVariants` beyond `knight_red`.
