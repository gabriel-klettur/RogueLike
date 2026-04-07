# Valkur – Python-to-Unity Migration Workspace

## Project Context

This workspace contains a **roguelike 2D action game ("Valkur")** being migrated from **Python (Pygame-CE)** to **Unity 2022.3 LTS (URP 2D / C#)**. The migration is ~82% complete (41/50 steps).

### Workspace Layout

| Path | Purpose |
|------|---------|
| `python/src/` | Python source: `roguelike_engine/`, `roguelike_game/`, `roguelike_ui/`, `roguelike_editors/`, `minigames/` |
| `python/data/` | JSON game data: entities, spells, items, config, maps |
| `python/assets/` | Sprites, audio, VFX (PNG, WAV/OGG) |
| `python/schemas/` | JSON Schema validation files |
| `python/tests/` | Pytest suite |
| `unity/Valkur/` | Unity 2022.3 URP 2D project |
| `unity/Valkur/Assets/_Project/` | Primary game code & assets |
| `unity/docs/Migration_python_to_unity/` | Migration documentation |

### Architecture (Python → Unity Mapping)

| Python | Unity |
|--------|-------|
| `Game` + `LoopManager` | `GameBootstrap` + `GameDirector` |
| `ECSWorld` (custom ECS) | MonoBehaviour components + service locator |
| `system_registry.py` (update/render systems) | C# systems, Update/LateUpdate order |
| `SpatialIndex` | `SpatialHash.cs` |
| `ShutdownManager` | `SaveService` + `SaveSchemaMigrator` |
| `component_registry.py` (45+ components) | C# components, ScriptableObjects |
| JSON configs (`data/`) | ScriptableObjects + `_Project/Data/` |
| `FSMSystem` + state files | `StateMachine.cs` + `FSMMonsterBrain.cs` |

### Unity Assembly Structure

- `Valkur.Core` – Services, bootstrap, singletons
- `Valkur.Data` – ScriptableObjects, DTOs
- `Valkur.Gameplay` – Game logic, entities, combat, spells
- `Valkur.Infrastructure` – Audio, persistence
- `Valkur.UI` – Menus, HUD
- `Valkur.Editor` – Editor-only tools (migration, atlas builders)

## Coding Conventions

### C# (Unity)

- Target: Unity 2022.3 LTS, URP 2D renderer, New Input System
- Use `SerializeField` for inspector fields; avoid public fields
- Use ScriptableObjects for data catalogs (monsters, spells, items, players)
- Follow existing assembly structure (`Valkur.Core`, etc.)
- Use `ServiceLocator` for dependency access (no raw singletons)
- Pool frequently spawned objects via `ObjectPool.cs`
- Use 15 sorting layers (Background → Overlay) for depth
- Physics layers: Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15)
- Write `[Tooltip("...")]` on serialized fields

### Python (Reference)

- Pygame-CE based; custom ECS with dicts
- Pydantic models for item/spell validation
- SQLAlchemy + Alembic for entity DB
- JSON data files under `python/data/`
- Tests with pytest; headless Pygame fixtures

## Migration Rules

1. **Never modify Python source** unless explicitly asked – it is the reference implementation.
2. **Always check existing Unity scripts** before creating new ones; avoid duplicate systems.
3. **Preserve game feel**: timing, speed, damage formulas must match Python values.
4. **Data-driven**: game tuning lives in ScriptableObjects/JSON, not hardcoded in C#.
5. **Reference the roadmap**: `unity/docs/Migration_python_to_unity/01_execution/roadmap_50_steps.md`
6. **Log migration decisions** in documentation when making architectural choices.

## Known Issues

- **CRITICAL**: Physics2D Layer Collision Matrix is all-to-all (P2.1 – must fix)
- **Material leaks**: Runtime `new Material()` without cleanup in WorldGridBuilder, TileEditorGridCursor
- **Asset Phase 2 deferred**: asset_map.csv incomplete, sprite atlas consolidation pending
- **Audio**: Pipeline empty, no sounds integrated yet
- **Input System**: Legacy InputManager axes still wired alongside New Input System
