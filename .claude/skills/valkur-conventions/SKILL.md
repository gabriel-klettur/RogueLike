---
name: valkur-conventions
description: One-stop quick reference for Valkur conventions — assemblies, layers, sorting, code style, the cardinal "console must be clean" rule, key gotchas, Python→Unity unit conversions. Load this skill at the start of any non-trivial task to keep the rules close to hand.
---

# Valkur Conventions — Quick Reference

> The longer form lives in [CLAUDE.md](../../../CLAUDE.md) and [.github/skills/unity-development/SKILL.md](../../../.github/skills/unity-development/SKILL.md). This is the fast-lookup card.

## Cardinal rules

1. **Unity MCP console must be clean** (zero errors, zero actionable warnings) before declaring any task complete. Run `mcp_unity_refresh_unity` (compile=request, mode=force, scope=scripts, wait_for_ready=true) then `mcp_unity_read_console` (types=["error","warning"], format=detailed) after every C# change. Also check the terminal log if Unity batch / test runner just ran.
2. **Never modify** `python/src/` or `unity/Udemy_Inspiration/`.
3. **Check existing scripts** before creating new ones (`Grep` first).
4. **Preserve numerical parity** with Python (damage, speed, cooldowns, AoE radii, AI timings).

## Assembly rules

| Path | Assembly | May reference |
|---|---|---|
| `Scripts/Core/` | `Valkur.Core` | — |
| `Scripts/Data/` | `Valkur.Data` | Core |
| `Scripts/Infrastructure/` | `Valkur.Infrastructure` | Core, Data |
| `Scripts/Gameplay/` | `Valkur.Gameplay` | Core, Data, Infrastructure |
| `Scripts/UI/` | `Valkur.UI` | Core, Data, Infrastructure |
| `Scripts/Editor/` | `Valkur.Editor` | All above |

**Forbidden:** `Valkur.Gameplay → Valkur.UI` (would create a cycle). Use `ServiceLocator` or `GameEvents` for cross-system signaling.

## Code style

- `[SerializeField] private` + `[Tooltip("…")]`. **Never public fields.**
- `ServiceLocator.Get<T>()`. **Never raw singletons.** `SingletonMonoBehaviour<T>` only for true scene-wide managers.
- ScriptableObjects for designer-tunable data. **No hardcoded tuning.**
- ObjectPool for hot-path spawns (projectiles, VFX, hit numbers).
- `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset for any static mutable state (Domain Reload is OFF).
- `_camelCase` private fields, `PascalCase` properties/methods. One class per file.

## Layers

**Physics:** Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15).

**Sorting (depth):** Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead → UI_World → Overlay.

## Python → Unity conversions

| Python | Unity | Formula |
|---|---|---|
| px | world units | `÷ 16` (PPU=16; Buildings PPU=32) |
| px/tick (60Hz) | world units/s | `× 3.75` |
| px/tick² | world units/s² | `× 225` |
| ticks | seconds | `÷ 60` |

## The pit traps

| Bug | Cause | Fix |
|---|---|---|
| `MissingReferenceException` after second Play | Static field holds destroyed Object | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset |
| Camera doesn't pan | Cinemachine `Follow` overrides every LateUpdate | `CameraSetup.DetachFollow()` |
| Player attacks while editor open | Missing input guard | `if (GameEditorManager.AnyEditorActive) return;` |
| `InventorySlot == null` won't compile | Struct, not class | `.IsEmpty` |
| `SpellDefinition.cooldown` doesn't exist | Renamed | `cooldownDuration` |
| `Health.Current` doesn't exist | Renamed | `CurrentHp` |
| `DashAbility` not in `Player` namespace | Lives in `Valkur.Gameplay.Combat` | Update `using` |
| Zone lookup fails on case mismatch | `OrdinalIgnoreCase` | Pass consistent casing anyway |
| Custom GL drawing invisible in URP | `Camera.current` null in URP | `RenderPipelineManager.endCameraRendering` |
| TMP NullRef on Buttons | `Image` + `TMP_Text` on same GO | Split into parent + child |
| EditMode "leaked materials" | `.material` clones on use | Use `.sharedMaterial` or `LogAssert.ignoreFailingMessages = true` |
| Black tiles | Sprite-Lit-Default × no Light2D = ×0 | `Sprite-Unlit-Default` fallback |

## Mandatory post-edit

1. `mcp_unity_refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)`
2. `mcp_unity_read_console(types=["error","warning"], page_size=50, format="detailed", include_stacktrace=true)`
3. Fix every error and every actionable warning.
4. Acceptable warnings only: MCP WebSocket reconnect, "Default GameObject Tag X already registered".
5. Never report "done" with a dirty console.
