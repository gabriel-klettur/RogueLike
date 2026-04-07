---
description: "Python roguelike reference code conventions. Applied when reading Python source. Reminds that Python files are READ-ONLY reference implementations and must not be modified."
applyTo: "python/src/**/*.py"
---

## Python Reference Code — READ ONLY

These Python files are the **reference implementation** for the Valkur migration.

### Rules
- **DO NOT modify** Python source files unless explicitly asked
- Use these files to understand the exact algorithms, formulas, and timing values
- All numerical constants from Python must be preserved exactly in the Unity port
- Check `python/tests/` for expected behavior when porting any system

### Key Locations
- `roguelike_game/ecs/core/component_registry.py` — All 45+ component definitions
- `roguelike_game/ecs/core/system_registry.py` — System execution order
- `roguelike_game/ecs/systems/` — All game systems (combat, spells, AI, inventory)
- `roguelike_engine/` — Engine systems (map, tile, rendering, input, database)
- `roguelike_game/config/spells_config.py` — Spell definitions and defaults
