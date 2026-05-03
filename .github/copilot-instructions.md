# Valkur – Python-to-Unity Migration Workspace

## Project Context

**Valkur** is a 2D roguelike action game being migrated from **Python (Pygame-CE)** to **Unity 2022.3.62f1 LTS (URP 2D / C#)**. **Core gameplay migration is ~98% complete**; remaining work is asset-pipeline atlas consolidation plus a few low-priority Python sub-systems (`burn_system`, `item_factory`, three config JSONs). Pylos and Soluna minigames are **permanently deprecated** — never propose, plan, or list them. See [`MIGRATION_GUIDE.md`](MIGRATION_GUIDE.md) (canonical) and [the roadmap](unity/docs/Migration_python_to_unity/01_execution/roadmap_50_steps.md) (historical) for details.

### Workspace Layout

| Path | Purpose |
|------|---------|
| `python/src/` | **READ-ONLY** reference: `roguelike_engine/`, `roguelike_game/`, `roguelike_ui/`, `roguelike_editors/` |
| `python/data/` | JSON game data (entities, spells, items, maps) — consumed by Unity importers |
| `python/assets/` | Source sprites, audio, VFX |
| `python/tests/` | Pytest suite (behavior reference) |
| `unity/Valkur/Assets/_Project/` | Primary Unity code & assets |
| `unity/docs/Migration_python_to_unity/` | Migration docs, audits, roadmap |

Detailed architecture and system-by-system mapping: [MIGRATION_GUIDE.md](.github/MIGRATION_GUIDE.md)

### Unity Assemblies

| Assembly | Path | Purpose |
|----------|------|---------|
| `Valkur.Core` | `Scripts/Core/` | Services, bootstrap, `ServiceLocator` |
| `Valkur.Data` | `Scripts/Data/` | ScriptableObjects, DTOs |
| `Valkur.Gameplay` | `Scripts/Gameplay/` | Combat, spells, AI, entities, world |
| `Valkur.Infrastructure` | `Scripts/Infrastructure/` | Audio, persistence |
| `Valkur.UI` | `Scripts/UI/` | Menus, HUD |
| `Valkur.Editor` | `Scripts/Editor/` | Migration importers, editor tools |

**Dependency rule:** `Valkur.Gameplay` cannot reference `Valkur.UI` (circular dependency). Both can reference `Core`, `Data`, `Infrastructure`.

## Build & Test

```bash
# Python tests
cd python && python -m pytest tests/ -v

# Unity tests (CLI)
"C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" -runTests -testPlatform EditMode -projectPath unity/Valkur

# Data validation (in Unity Editor)
# Menu: Valkur > Migration > Dry-Run All
```

## Coding Conventions

### C# (Unity)

- `[SerializeField]` + `[Tooltip("...")]` for inspector fields; no public fields
- `ServiceLocator` for dependency access — no raw singletons
- ScriptableObjects for all data catalogs (monsters, spells, items, players, audio)
- Object pooling via `ObjectPool.cs` for frequently spawned objects
- Physics layers: Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15)
- 15 sorting layers: Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead → UI_World → Overlay

### Python (Reference)

- **DO NOT modify** unless explicitly asked — it is the migration source of truth
- Pygame-CE + custom dict-based ECS (45+ components)
- JSON data under `python/data/`; Pydantic for validation

## Migration Rules

1. **Never modify Python source** unless explicitly asked.
2. **Always check existing Unity scripts** before creating new ones; avoid duplicates.
3. **Preserve game feel**: timing, speed, damage formulas must match Python values exactly.
4. **Data-driven**: game tuning lives in ScriptableObjects/JSON, not hardcoded in C#.
5. **Reference the roadmap**: `unity/docs/Migration_python_to_unity/01_execution/roadmap_50_steps.md`

## Unit Conversions (Python → Unity)

| Python | Unity | Formula |
|--------|-------|---------|
| Pixels | World units | `px / 16` (PPU = 16) |
| px/tick speed | world units/s | `px_per_tick × 60 / 16` or `× 3.75` |
| px/tick² accel | world units/s² | `px_per_tick² × 3600 / 16` |
| Ticks duration | Seconds | `ticks / 60` |

## Key Gotchas

- **Image + TMP on same GameObject**: Causes `NullReferenceException`. Use parent (Image+Button) + child (TMP) pattern.
- **InventorySlot is a struct**: Cannot compare to `null`; use `.IsEmpty` instead.
- **EditMode tests + renderer.material**: Causes leak warnings. Use `LogAssert.ignoreFailingMessages = true`.
- **SpellDefinition API**: Use `cooldownDuration` (not `cooldown`), `Health.CurrentHp` (not `Current`).
- **DashAbility namespace**: Lives in `Valkur.Gameplay.Combat` (not Player).
- **Zone name case**: `ZoneManager` uses `StringComparer.OrdinalIgnoreCase` — always use consistent casing.

## Open Work

- **Paso 4**: Record Python baseline evidence (video + captures) — manual execution required
- **Pasos 15, 20–22**: Formal naming convention + batch asset migration with visual validation
- **Asset Phase 2**: sprite atlas consolidation, `asset_map.csv` full population
- **Input System**: Legacy InputManager axes still wired alongside New Input System
- **Minigames**: Pylos (~2000 lines) deferred as separate game; Soluna is empty placeholder
