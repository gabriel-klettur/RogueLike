 # FSM for Spawners – Design and Implementation (Phased)
 Location: `data/spawners/FSM_spawners.md`

 ## Goal
Model the lifecycle of spawners as a finite state machine (FSM) to make behavior explicit, testable, and reusable while keeping the current JSON intact in Phase 1.

 ## Scope (Phase 1)
Phase 1 introduces an observational/derived FSM that mirrors existing spawner behavior without altering JSON schemas or public APIs. No external dependency on the global FSM Editor is required yet. The FSM state is exposed on each spawner entity via `SpawnerState.fsm_state` for debugging/telemetry.

 ## States (Phase 1)
- await_trigger
  The spawner is idle waiting to be started (proximity or auto). Also used when there are no waves configured.

- spawning_wave
  The system is placing spawn requests for the current wave.

- wait_cooldown
  Between waves or batches, waiting for a cooldown to elapse. If `between_waves_cooldown_frames > 0`, this fixed cooldown is preferred and is shown as `bwc` in the debug overlay; otherwise, `cooldown_frames` is used.

- wait_clear
  In advance-on-clear mode, the wave has already been placed and we are waiting until all spawned entities (of the current wave or globally, depending on the policy) are eliminated.

- wait_restart
  After finishing all waves when looping is enabled (loop|repeat|restart_on_done), waiting on `restart_cooldown_frames` to restart from wave 0.

- finished
  All waves are completed and there is no looping. The spawner remains done.

 ## Transitions (informal, derived from existing runtime logic)
- await_trigger -> spawning_wave
  Condition: `SpawnerState.started` is True and there are waves configured.

- spawning_wave -> wait_cooldown
  Condition: wave placement succeeds (even if 0 were placed, the system may advance immediately and set cooldown for next wave unless end-of-sequence).

- spawning_wave -> finished
  Condition: end-of-sequence reached with no looping.

- spawning_wave -> wait_restart
  Condition: end-of-sequence reached with looping; schedule `restart_cooldown_frames` and enter `wait_restart`.

- wait_cooldown -> spawning_wave
  Condition: `cooldown_remaining == 0` and next wave is available.

- wait_cooldown -> finished | wait_restart
  Condition: If the cooldown completion aligns with end-of-sequence and looping, go to `wait_restart`; otherwise, `finished`.

- wait_clear -> spawning_wave
  Condition: advance_on == 'clear' and the current set of tracked entities is empty, AND next wave exists.

- wait_clear -> finished | wait_restart
  Condition: end-of-sequence reached; branch based on looping.

- wait_restart -> await_trigger | spawning_wave
  Condition: `restart_cooldown_remaining == 0`. If mixed proximity mode applies (see below), we reset `started=False` and `initial_proximity_done=False`, returning to `await_trigger`; otherwise we continue spawning immediately.

 ## Mapping to JSON fields (kept intact in Phase 1)
- trigger.type
  - 'auto': Trigger system sets `SpawnerState.started = True`.
  - 'proximity': Based on player distance to `SpawnerConfig.anchor_tile` within `trigger.radius` (tiles). See mixed mode below.

- policy.proximity_initial_only (hybrid mode)
  When True (or when `between_waves_cooldown_frames > 0`), proximity is only used for the initial start. `SpawnerState.initial_proximity_done` latches after first start; proximity is ignored for subsequent waves. On loop restart, the latch is reset.

- policy.advance_on ('clear' | 'cooldown')
  - 'clear': After placing a wave, wait for live entities to be eliminated before advancing (state: wait_clear).
  - 'cooldown': Spawn all configured waves separated by cooldowns; spawner completes after the last cooldown and waits for global active to clear before finishing/restarting.

- policy.max_active (int)
  Enforced via `SpawnerState.active_entities` to cap total living spawns across waves. This influences how many entities can be placed in a wave but does not define a unique FSM state; it affects capacity calculations during `spawning_wave`.

- cooldowns
  - `cooldown_frames` (derived from policy.cooldown_s)
  - `between_waves_cooldown_frames` (derived from policy.between_waves_cooldown_s)
  - `restart_cooldown_frames` (derived from policy.restart_cooldown_s)
  The runtime prefers `between_waves_cooldown_frames` when present for fixed delays between waves.

- looping
  Policy keys `loop | repeat | restart_on_done` enable loop behavior. On end-of-sequence, the FSM enters `wait_restart` using `restart_cooldown_frames`.

 ## Notes on unsupported/optional fields
- persistent: If present in future templates, it can indicate whether the spawner retains `finished` across map reloads or session boundaries. Phase 1 does not change persistence semantics; use default behavior.

 ## Where it is implemented (Phase 1)
- `src/roguelike_game/ecs/components/spawner/spawner_state.py`
  Added field `fsm_state: str = "await_trigger"`.

- `src/roguelike_game/ecs/systems/spawner/spawner_system.py`
  Sets `fsm_state` at key lifecycle points without altering existing behavior.
  Examples:
    - not started -> 'await_trigger'
    - cooldown_remaining > 0 -> 'wait_cooldown'
    - waiting for clears -> 'wait_clear'
    - spawning path -> 'spawning_wave'
    - finished without loop -> 'finished'
    - loop end -> 'wait_restart'

- `src/roguelike_game/ecs/systems/rendering/spawner_debug_system.py`
  Debug box now shows `fsm: <state>` together with cooldown labels (cd/bwc/rc), waves, and shape.

 ## How to view FSM state in-game
- Ensure the Spawner Debug overlay is enabled: `config.DEBUG_SPAWNER = True`.
- Hover or select a spawner to see the info box near its anchor.
- The box shows: template id, ON/OFF/DONE, wave X/Y, live/expected + cooldown (`cd|bwc|rc`), `fsm: <state>`, mode/loop/shape.

 ## Testing checklist (Phase 1)
- auto trigger + single wave -> fsm: spawning_wave -> finished
- proximity trigger outside radius -> await_trigger; inside -> spawning_wave
- proximity_initial_only + between_waves_cooldown -> fsm shows bwc after wave, proximity ignored until loop restart
- loop with restart_cooldown -> wait_restart then either await_trigger (mixed mode) or immediate spawning
- max_active limits wave placements without breaking FSM state updates
- advance_on='cooldown' spawns all waves separated by cooldowns, then waits to finish

 ## Phase 2 – Compile templates to FSM Sets (implemented)
- Reusable FSM sets defined under `data/fsm/sets.json`:
  - `Spawner_Periodic_Cooldown`
  - `Spawner_Waves_Clear`
  - `Spawner_Periodic_BetweenWaves`
- `SpawnerPlacementSystem._compile_fsm_set(cfg)` derives the set id and parameters from resolved config fields:
  - Decision: `advance_on == 'clear'` -> `Spawner_Waves_Clear`; else if `between_waves_cooldown_frames > 0` -> `Spawner_Periodic_BetweenWaves`; else `Spawner_Periodic_Cooldown`.
  - Parameters include: `trigger`, `advance_on`, `cooldown_frames`, `between_waves_cooldown_frames`, `restart_cooldown_frames`, `proximity_initial_only`, `loop`, `max_active`, `mode`, `spawner_shape`, `spawn_radius`, `template_id`.
- `SpawnerState` now has `fsm_set_id` and `fsm_set_params` to expose the compiled assignment for Editor/overlay use.
- Runtime behavior remains unchanged; this metadata is informational.

 ## Phase 3 – Per-template/instance FSM override (implemented)
- You can override the compiled set and/or parameters at template or instance level.
- Supported forms:
  1) Template block in `spawners_templates.json`:
     ```json
     {
       "id": "survival_10",
       "fsm": { "set_id": "Spawner_Waves_Clear", "params": { "note": "forced clear mode" } }
     }
     ```
  2) Instance block in `spawners_instances.json`:
     ```json
     { "template_id": "survival_10", "fsm": { "set_id": "Spawner_Periodic_Cooldown", "params": { "cooldown_frames": 180 } } }
     ```
  3) Instance dot-notation overrides inside `overrides`:
     ```json
     { "template_id": "survival_10", "overrides": { "fsm.set_id": "Spawner_Periodic_BetweenWaves", "fsm.params.tag": "boss_room" } }
     ```
- Precedence: instance overrides > template block > compiled defaults.
- The debug overlay now shows `set: <fsm_set_id>` alongside `fsm: <state>`.
- Future work (optional): add schema validation for the `fsm` block and linter warnings.

 ## Rationale
- Explicit state improves debuggability and future test automation.
- Phase 1 is non-invasive: no schema changes, no global FSM dependency.
- Phases 2/3 align spawner behavior with the existing FSM Editor roadmap for professional workflows, sets, and assignments.

 ## Appendix – State meanings (concise)
await_trigger: not started or no waves
spawning_wave: issuing spawn requests
wait_cooldown: waiting for next wave (cd/bwc)
wait_clear: waiting for living entities to clear (advance_on=clear)
wait_restart: looping delay before restarting
finished: completed without loop

 ## ¿Quieres que añada algo más?
 - Tabla de contenidos automática.
 - Anclas/enlaces internos por sección.
