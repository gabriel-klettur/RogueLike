# Valkur — Migration Guide (archived)

> **Status:** The Python → Unity migration is complete. This file is retained as
> a brief archive note. The Unity project is the canonical implementation;
> there is no Python source in `main` anymore.

## Where the original Python implementation lives

The original Pygame-CE prototype was archived as a git tag before deletion:

```bash
git checkout archive/python-legacy-2026-05-06
```

That checkout restores the full `python/` tree (engine, gameplay ECS, in-game
editors, JSON data, pytest suite, scripts) at the state immediately before the
final cutover. Useful for:

- Disaster recovery if a Unity-side `.asset` is corrupted and a stale snapshot
  is needed.
- Historical research on a behavior that was lost in translation.
- Comparing numerical values when balancing.

It is **not** a runnable second implementation maintained alongside Unity. Once
checked out, treat it as read-only frozen-in-time reference.

## What was migrated

Every gameplay-relevant subsystem ended up with a Unity equivalent. A few
surface-level highlights:

| Subsystem | Unity location |
|---|---|
| Combat (melee, projectiles, area, dash, slash, beam, mine, puddle, shield, summon, totem, vortex, wall, meteor, cone, arcane flame) | `Scripts/Gameplay/Spells/{Core,Executors,Controllers,Projectiles,Visuals}/` |
| Status effects (burn, poison, stun, freeze, slow) | `Scripts/Gameplay/Combat/StatusEffects/` |
| FSM monster AI + boss phases | `Scripts/Gameplay/Enemies/` + `StreamingAssets/FSM/*.json` (F12 editor) |
| Tilemap world + Y-sort | `Scripts/Gameplay/World/{Setup,Navigation}/` |
| Buildings (templates + instances + colliders) | `Scripts/Gameplay/World/Buildings/` + `Data/Catalogs/Buildings/` + `StreamingAssets/Buildings/` |
| Zones / portals / overlays | `Scripts/Gameplay/World/Zones/` + `StreamingAssets/Maps/` |
| Inventory + item use (HP/Mana/Energy/Hunger restoration) | `Scripts/Gameplay/Inventory/` |
| Quests + skill tree + chat | `Scripts/Gameplay/Quests/` + `Player/LearnedSkills.cs` + `Gameplay/Chat/` |
| Save/load + profile + telemetry | `Scripts/Gameplay/Save/` + `Scripts/Infrastructure/Persistence/Profile/` |
| Audio (music + SFX + scopes + ducking + beat clock) | `Resources/AudioCatalog.asset` + `Scripts/Infrastructure/AudioManager*.cs` |
| Day/night cycle + Light2D rigs | `Scripts/Gameplay/World/Lighting/` |
| In-game runtime editors (F1/F3/F4/F5/F6/F7/F8/F10/F11/F12 + Ctrl+F3) | `Scripts/Gameplay/Editors/*/` |
| Combo counter (with `allowed_sources` / `min_damage` / `require_enemy` / `require_unique_target` rules) | `Scripts/Gameplay/Combat/Mechanics/ComboCounter.cs` |

## What's intentionally NOT in Unity

- `roguelike_engine/audio/`, `camera/`, `input/`, `tile/`, `z_layer/`, `cache/`,
  `console/`, `diagnostics/` — Pygame-specific renderer code, replaced by Unity
  equivalents (Cinemachine, InputSystem, Tilemap, URP 2D, `DevConsole`).
- `roguelike_engine/minimap/` — minimap is intentionally not implemented in
  Unity.
- `minigames/` (Pylos, Soluna) — permanently deprecated, never to be revived.

## What survives outside `python/`

Standalone utilities that operate on the Unity project moved to `tools/`:

- `tools/audio/` — BPM + key analysis (librosa) and AudioCatalog patcher.
- `tools/atlas/` — tile size auditor, normalizer, asset audit, atlas doc gen.
- `tools/world/` — overlay bootstrap utility.

These are pure stdlib + Pillow + librosa; they do not depend on the archived
`python/src/` engine code.

## Where to read the canonical project rules

[CLAUDE.md](../CLAUDE.md) at the repo root is the single source of truth for
Unity conventions, assembly rules, layer numbers, code style, gotchas, and
where data lives.
