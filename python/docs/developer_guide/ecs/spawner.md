# Spawner System

This document describes the spawner system, its configuration keys, runtime state, and debug rendering. It includes the new hybrid trigger behavior: proximity for the initial start, then fixed cooldown between waves.

---

## Components

- SpawnerConfig
  - trigger: { type: "proximity" | "auto", radius?, auto_start? }
  - policy: periodic mode keys
    - cooldown_s: base per-wave cooldown
    - restart_on_done | loop | repeat: enable looping
    - restart_cooldown_s: cooldown before restarting after all waves
    - max_active: cap of simultaneously alive entities from this spawner
    - advance_on: "clear" (default) or "cooldown"
    - proximity_initial_only: if true, proximity is only used to start the first wave
    - between_waves_cooldown_s: optional fixed cooldown between waves (seconds)
  - waves or waves_id: list of wave dicts or reference id from spawners_waves.json
  - spawn_radius: 0/None (legacy spiral) | number of tiles > 0 | "random" (random-in-circle per wave fallback)
  - spawner_shape: "circle" (default) | "square" (area shape when using random placement)
  - defend_spawn, defend_leash: optional NPC defend behavior around the spawn area
  - Derived (computed at load, 60 FPS):
    - cooldown_frames
    - restart_cooldown_frames
    - between_waves_cooldown_frames

- SpawnerState
  - started: whether the spawner logic is active
  - current_wave_idx, spawned_this_wave, expected_this_wave, current_wave_entities
  - cooldown_remaining: frames until next spawn step
  - restart_cooldown_remaining: frames until loop restart
  - active_entities: live ids used for max_active enforcement
  - initial_proximity_done: NEW flag latching that the initial proximity trigger has fired

---

## Hybrid trigger: proximity initial, fixed cooldown between waves

- Set policy.proximity_initial_only: true to enable proximity gating only for the first start.
- Optionally set policy.between_waves_cooldown_s to a fixed delay (seconds) used between waves.
- Behavior:
  - Before first wave: proximity trigger must be satisfied to start. When it starts, SpawnerState.initial_proximity_done = true and started = true.
  - Between waves: proximity is ignored. The next wave waits for between_waves_cooldown_s (if set) or falls back to cooldown_s.
  - Loop restarts (restart_on_done): after restart_cooldown_s, proximity latch resets (initial_proximity_done = false, started = false) so the player must re-enter proximity to start again.

This preserves backward compatibility for existing spawners: if between_waves_cooldown_s is not set and proximity_initial_only is false, behavior remains unchanged.

---

## Debug rendering

With DEBUG_SPAWNER enabled, the overlay shows:
- Anchor and area (spawn_radius + spawner_shape)
- Wave info, live/expected counts
- Cooldowns:
  - "cd Xs": regular per-wave cooldown countdown
  - "rc Xs": restart cooldown before looping
  - "bwc Xs": NEW fixed between-waves cooldown countdown

---

## Example template

```json
{
  "id": "survival_10",
  "spawner_type": "invisible",
  "spawner_shape": "square",
  "spawn_radius": 20,
  "defend_spawn": true,
  "defend_leash": true,
  "trigger": { "type": "proximity", "radius": 10, "auto_start": true },
  "policy": {
    "mode": "periodic",
    "cooldown_s": 1.0,
    "proximity_initial_only": true,
    "between_waves_cooldown_s": 10.0,
    "max_active": 0,
    "persistent": false,
    "restart_on_done": false
  },
  "waves_id": "waves_survival_10"
}
```

---

## Notes

- Time-based values are parsed at load time assuming 60 FPS into frame counters in SpawnerConfig.
- If advance_on: "cooldown" is used, waves spawn back-to-back on cooldown; hybrid proximity gating still applies only to the initial start.
