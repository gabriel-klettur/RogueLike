---
name: python-to-csharp
description: Translate Python (Pygame-CE) game code to Unity C# for the Valkur migration. Covers Pygame→Unity API mapping, ECS→MonoBehaviour conversion, Pydantic→ScriptableObject, dict→class. Load when porting a specific Python file or system.
---

# Python → C# Translation

Full canonical reference:

**[.github/skills/python-to-csharp/SKILL.md](../../../.github/skills/python-to-csharp/SKILL.md)**

## Quick translation tables

### Core types

| Python | C# / Unity |
|---|---|
| `dict` | `Dictionary<K,V>` or class |
| `list` | `List<T>` |
| `set` | `HashSet<T>` |
| `tuple` | `(T1, T2)` or `Vector2/Vector3` |
| `Optional[T]` | `T?` (nullable) |
| `dataclass` | `class` or `struct` |
| Pydantic `BaseModel` | `ScriptableObject` or `[Serializable] class` |

### Pygame → Unity

| Pygame | Unity |
|---|---|
| `pygame.Surface` | `Sprite` / `SpriteRenderer` |
| `surface.blit()` | SpriteRenderer on a GameObject |
| `pygame.Rect` | `Rect` / `Bounds` / `Collider2D` |
| `pygame.time.Clock` | `Time.deltaTime` |
| `pygame.event.get()` | New Input System / `Update()` |
| `pygame.key.get_pressed()` | `InputAction.ReadValue<>()` |
| `pygame.mouse.get_pos()` | `Camera.main.ScreenToWorldPoint()` |
| `pygame.draw.circle()` | `LineRenderer` / `Gizmos` / VFX |
| `pygame.mixer.Sound` | `AudioSource.PlayOneShot()` (via `IAudioService`) |
| `pygame.image.load()` | `Resources.Load<Sprite>()` / Addressables |

### ECS

| Python ECS | Unity |
|---|---|
| Entity (int ID) | `GameObject` |
| Component (dict entry) | `MonoBehaviour` or `[Serializable] class` |
| System (function) | Manager class / `MonoBehaviour.Update()` |
| `world.add_component(eid, comp)` | `gameObject.AddComponent<T>()` |
| `world.entities_with(type)` | `FindObjectsOfType<T>()` / spatial query / `EntityRegistry` |
| `SpatialIndex.query_radius()` | `SpatialHash.QueryRadius()` |

### Numerical conversions (preserve game feel)

| Python | Unity | Formula |
|---|---|---|
| px | world units | `÷ 16` (PPU=16; Buildings PPU=32) |
| px/tick (60Hz) | world units/s | `× 3.75` |
| px/tick² | world units/s² | `× 225` |
| ticks | seconds | `÷ 60` |

## Procedure

1. Read Python source completely.
2. Identify public interface (inputs, outputs, events, side effects).
3. Map every construct using the tables above.
4. Preserve numerical constants exactly.
5. Place file in correct assembly folder (see `unity-development` skill).
6. Add `[SerializeField]` + `[Tooltip]` on serialized fields.
7. Verify no existing Unity script already covers this functionality (`Grep` first).

## Common gotchas

- Python uses **radians**; Unity APIs mostly use **degrees**, but `Mathf.Sin/Cos` use radians.
- Python dicts ordered (3.7+); C# `Dictionary` is not — use `OrderedDictionary` or `List` if order matters.
- Pygame Y goes **down**; Unity Y goes **up** — flip Y.
- Python `//` is integer division; C# `int / int` is already integer division.
- Python `range(a, b)` excludes `b`; mirror in C# `for` loops.
- Pygame coords are **pixels**; Unity is **world units** (÷ PPU).
- Python `time.time()` → `Time.time` in Unity.

## Mandatory post-implementation verification

After every C# write/edit:

1. `mcp_unity_refresh_unity(compile="request", mode="force", scope="scripts", wait_for_ready=true)`
2. `mcp_unity_read_console(types=["error","warning"], page_size=50, format="detailed", include_stacktrace=true)`
3. Fix every error before declaring done.
4. Benign warnings allowed only: MCP WebSocket reconnect, "Default GameObject Tag X already registered".
