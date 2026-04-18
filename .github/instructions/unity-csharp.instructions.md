---
description: "Unity C# coding conventions for the Valkur project. Applied when editing or creating C# scripts in the Unity project."
applyTo: "unity/**/*.cs"
---

## Unity C# Conventions for Valkur

### Assembly Placement
- `Scripts/Core/` → Valkur.Core (services, bootstrap, singletons)
- `Scripts/Data/` → Valkur.Data (ScriptableObjects, DTOs)
- `Scripts/Gameplay/` → Valkur.Gameplay (game logic, combat, spells, AI)
- `Scripts/Infrastructure/` → Valkur.Infrastructure (audio, persistence)
- `Scripts/UI/` → Valkur.UI (menus, HUD)
- `Scripts/Editor/` → Valkur.Editor (editor-only tools)

### Code Style
- `[SerializeField]` for inspector fields; never use public fields for data
- `[Tooltip("description")]` on all serialized fields
- `ServiceLocator` for dependency access — no raw singletons
- ScriptableObjects for data catalogs
- Object pooling via `ObjectPool.cs` for frequently spawned objects

### Physics Layers
Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15)

### Sorting Layers (depth order)
Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead → UI_World → Overlay

### Migration Rules
- Preserve exact numerical values from Python (damage, speed, timing)
- Check existing scripts before creating new ones
- Game tuning lives in ScriptableObjects, not hardcoded

### Post-Implementation Verification (MANDATORY)
After **every** Unity C# change (create, edit, refactor, delete):
1. Call `mcp_unity_refresh_unity` with `compile=request`, `mode=force`, `scope=scripts`, `wait_for_ready=true`
2. Call `mcp_unity_read_console` with `types=["error","warning"]`, `page_size=50`, `format=detailed`, `include_stacktrace=true`
3. If **any error** appears → fix immediately before proceeding
4. The only acceptable warnings are:
   - MCP WebSocket reconnect (benign after domain reload)
   - `Default GameObject Tag: X already registered` (editor state, not a code error)
5. **Never** report "done" until the console is error-free
