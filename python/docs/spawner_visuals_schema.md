# Spawner Visuals Schema (contract)

This document defines the required fields and invariants for building instances used as spawner visuals.

## Building instance (data/buildings/buildings_instances.json)

Required for any building used as a spawner visual:

- spawner_visual: true
- overrides._is_spawner_visual: true
- spawn_id: "<spawner_instance_id>"
- spawner_instance_id: "<spawner_instance_id>"
- overrides.spawner_instance_id: "<spawner_instance_id>"
- overrides.scale: [w, h] (optional; if the spawner visual state declares a scale)

Behavioral expectations (enforced by loader/runtime):

- Loaded hidden by default (runtime_hidden=True) and non-solid (solid=False).
- Colliders are not applied for these instances.
- They are kept in world memory so the spawner runtime can toggle visibility.

## Spawner instance (data/spawners/spawners_instances.json)

For each visual state key (e.g., "AwaitTrigger", "WaitCooldown"):

- visuals.<StateKey>.instance_id: <int>  (must reference an existing building instance id)
- visuals.<StateKey>.template_id: <int>  (template of the referenced building)
- visuals.<StateKey>.offset: [dx, dy]    (optional; pixel offset applied when visible)
- visuals.<StateKey>.scale: [w, h]       (optional; persisted in the building instance)

Global overrides:

- overrides.visible_in_game: true (spawner visuals are meant to be shown in game)

## Preflight and auto-repair

- On hot-reload (and via scripts/preflight_cli.py), preflight creates missing building instances for any visuals mapping and persists them to buildings_instances.json.
- Preflight enforces schema flags on existing referenced instances (spawner_visual=true, overrides._is_spawner_visual=true, spawn linkages, and scale when present).
- Editor flows also create or retag instances accordingly when a new state visual is assigned.

## Editor-only multi-preview gating

- Multi-preview showing all state visuals only occurs when both:
  - world.state.spawner_editor_active = True
  - config.DEBUG_SPAWNER = True

## Invariants summary

- Exactly one visual building is visible per spawner at runtime.
- All referenced instance_id exist and are tagged as spawner visuals.
- Spawner visuals are hidden + non-solid by default.
- Offsets/scales are respected; scales are mirrored in the building instance overrides.
