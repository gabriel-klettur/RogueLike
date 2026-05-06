# Valkur — 2D Roguelike (Unity)

## Project Context

**Valkur** is a 2D roguelike action game built on **Unity 2022.3.62f1 LTS (URP 2D / C#)**. It was originally prototyped in Python (Pygame-CE); the Unity port is **complete** and the original Python implementation has been archived (see the `archive/python-legacy-*` git tag for historical reference). Pylos and Soluna minigames are **permanently deprecated** — never propose, plan, or list them.

### Workspace Layout

| Path | Purpose |
|------|---------|
| `unity/Valkur/Assets/_Project/` | Primary Unity code & assets |
| `unity/Valkur/Assets/Tests/` | EditMode + PlayMode test suites |
| `unity/Udemy_Inspiration/DungeonGunnerCourse/` | Architectural reference (read-only) |
| `tools/` | Standalone Python utilities (audio analysis, atlas audits, overlay generation) |

### Unity Assemblies

| Assembly | Path | Purpose |
|----------|------|---------|
| `Valkur.Core` | `Scripts/Core/` | Services, bootstrap, `ServiceLocator` |
| `Valkur.Data` | `Scripts/Data/` | ScriptableObjects, DTOs |
| `Valkur.Infrastructure` | `Scripts/Infrastructure/` | Audio, persistence, profile DB |
| `Valkur.Gameplay` | `Scripts/Gameplay/` | Combat, spells, AI, entities, world |
| `Valkur.UI` | `Scripts/UI/` | Menus, HUD |
| `Valkur.Editor` | `Scripts/Editor/` | Editor tools (atlas builders, postprocessor, validators) |

**Dependency rule:** `Valkur.Gameplay` cannot reference `Valkur.UI` (would create a cycle). Both can reference `Core`, `Data`, `Infrastructure`. Cross-system signaling goes through `ServiceLocator` or `GameEvents`.

## Build & Test

```bash
# Unity tests via CLI
"C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" -runTests -testPlatform EditMode -projectPath unity/Valkur

# Or via MCP (preferred):
#   mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)
#   poll: mcp_unity_get_test_job(job_id=...)
```

## Coding Conventions (C#)

- `[SerializeField] private` + `[Tooltip("...")]` for inspector fields; **never** public fields
- `ServiceLocator.Get<T>()` for cross-system access; **no raw singletons** (only `SingletonMonoBehaviour<T>` for true scene-wide managers)
- ScriptableObjects for all designer-tunable data — no hardcoded tuning
- Object pooling via `Scripts/Core/ObjectPool.cs` for projectiles, VFX, hit numbers
- Static mutable state needs `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset (Domain Reload is OFF)
- One class per file; filename = class name

## Layers

**Physics:** Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15)

**Sorting (depth):** Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead → UI_World → Overlay

## Pixel-art Conventions

| Concept | Value | Notes |
|---|---|---|
| World PPU (most assets) | 16 | 1 world unit = 16 px |
| Buildings PPU | 32 | Higher PPU for finer detail |
| Tiles PPU | 32 | Audited via `tools/atlas/audit_tile_sizes.py` |

## Where Data Lives

| Data | Source of truth |
|---|---|
| Audio (music + SFX + scopes + ducking) | `Resources/AudioCatalog.asset` |
| Items / Monsters / Spells / Buildings / Particles / Spawners / Lighting / Vendors / Players | `Data/Catalogs/*/*.asset` |
| World state (placed buildings, lights, spawners, particles, tile overlays) | `StreamingAssets/{Buildings,Lights,Spawners,Particles,Maps}/*.json` (written by F1/F3/F8/F10/F11/Ctrl+F3) |
| Player saves + run history | `Application.persistentDataPath/{Saves,profile.json}` (atomic-write + checksum + 5 rotating backups) |

## Key Gotchas

- **Image + TMP on same GameObject**: Causes `NullReferenceException`. Use parent (Image+Button) + child (TMP) pattern.
- **InventorySlot is a struct**: Cannot compare to `null`; use `.IsEmpty` instead.
- **EditMode tests + renderer.material**: Causes leak warnings. Use `renderer.sharedMaterial` or `LogAssert.ignoreFailingMessages = true`.
- **SpellDefinition API**: Use `cooldownDuration` (not `cooldown`), `Health.CurrentHp` (not `Current`).
- **DashAbility namespace**: Lives in `Valkur.Gameplay.Combat` (not Player).
- **Zone name case**: `ZoneManager` uses `StringComparer.OrdinalIgnoreCase` — always use consistent casing.
- **Cinemachine**: Overrides `Camera.main.transform` every LateUpdate. Use `CameraSetup.DetachFollow()` to pan freely.
- **Custom GL drawing in URP**: Use `RenderPipelineManager.endCameraRendering`, not `OnRenderObject` (`Camera.current` is null in URP).

## Open Work

- **Asset pipeline Phase 2** — atlas consolidation + finalised `asset_map.csv` (formal naming + `SpriteAtlas` group build for the 9 planned domain atlases)
- **Boss music wiring** — `BossPhaseController.OnPhaseChanged` not yet wired to `AudioManager.PlayMusic`
