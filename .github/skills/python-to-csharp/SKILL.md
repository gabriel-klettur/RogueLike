---
name: python-to-csharp
description: "Translate Python game code to Unity C#. Use when porting a specific Python system, class, or algorithm to C#. Handles Pygame→Unity API mapping, ECS→MonoBehaviour conversion, dict→class translation, Pydantic→ScriptableObject conversion."
argument-hint: "Name the Python system or file to translate"
---

# Python-to-C# Translation

## When to Use
- Porting a specific Python system to Unity C#
- Converting Python data structures to C# equivalents
- Translating Pygame API calls to Unity equivalents

## API Translation Reference

### Core Types
| Python | C# (Unity) |
|--------|-----------|
| `dict` | `Dictionary<K,V>` or class |
| `list` | `List<T>` |
| `set` | `HashSet<T>` |
| `tuple` | `(T1, T2)` or `Vector2/Vector3` |
| `Optional[T]` | `T?` (nullable) or default |
| `dataclass` | `class` or `struct` |
| `Pydantic BaseModel` | `ScriptableObject` or `[Serializable] class` |

### Pygame → Unity
| Pygame | Unity |
|--------|-------|
| `pygame.Surface` | `Sprite` / `SpriteRenderer` |
| `surface.blit()` | SpriteRenderer on GameObject |
| `pygame.Rect` | `Rect` / `Bounds` / `Collider2D` |
| `pygame.time.Clock` | `Time.deltaTime` |
| `pygame.event.get()` | Input System / `Update()` |
| `pygame.key.get_pressed()` | `InputAction.ReadValue<>()` |
| `pygame.mouse.get_pos()` | `Camera.main.ScreenToWorldPoint()` |
| `pygame.draw.circle()` | `LineRenderer` / `Gizmos` / VFX |
| `pygame.mixer.Sound` | `AudioSource.PlayOneShot()` |
| `pygame.image.load()` | `Resources.Load<Sprite>()` / Addressables |

### ECS Translation
| Python ECS | Unity |
|------------|-------|
| Entity (int ID) | `GameObject` |
| Component (dict entry) | `MonoBehaviour` / `[Serializable] class` |
| System (function) | `MonoBehaviour.Update()` / manager class |
| `world.add_component(eid, comp)` | `gameObject.AddComponent<T>()` |
| `world.get_component(eid, type)` | `gameObject.GetComponent<T>()` |
| `world.entities_with(comp_type)` | `FindObjectsOfType<T>()` / spatial query |
| `SpatialIndex.query_radius()` | `SpatialHash.QueryRadius()` |

### Data Structures
| Python | C# |
|--------|-----|
| JSON config file | `ScriptableObject` asset |
| `component_registry` entries | C# MonoBehaviour scripts |
| `system_registry` ordering | Script Execution Order / manual update |
| SQLAlchemy Model | ScriptableObject or serializable class |
| Pydantic validators | `OnValidate()` / `[Range]` / custom Editor |

## Procedure

1. **Read** the Python source file completely
2. **Identify** the public interface (inputs, outputs, events)
3. **Map** each Python construct to its C# equivalent using tables above
4. **Preserve** all numerical constants exactly
5. **Place** the C# file in the correct assembly folder
6. **Add** `[SerializeField]` and `[Tooltip]` where appropriate
7. **Verify** no existing Unity script already covers this functionality

## Common Gotchas

- Python uses **radians** for math; Unity uses **degrees** for most APIs but radians for `Mathf.Sin/Cos`
- Python dicts are ordered (3.7+); C# `Dictionary` is not — use `OrderedDictionary` or `List` if order matters
- Pygame Y-axis goes **down**; Unity Y-axis goes **up** — flip Y where needed
- Python `//` is integer division; C# `/` on ints is already integer division
- Python `range(a, b)` excludes `b`; C# `for` loops must match
- Pygame coordinates are **pixels**; Unity uses **world units** (÷ PPU)
- Python `time.time()` returns seconds; use `Time.time` in Unity

## Post-Implementation Verification (MANDATORY)

After writing or editing any Unity C# script:

1. `mcp_unity_refresh_unity` — `compile=request`, `mode=force`, `scope=scripts`, `wait_for_ready=true`
2. `mcp_unity_read_console` — `types=["error","warning"]`, `page_size=50`, `format=detailed`, `include_stacktrace=true`
3. Fix any compilation errors before reporting completion
4. Only benign warnings allowed:
   - MCP WebSocket reconnect (domain reload artifact)
   - `Default GameObject Tag: X already registered`
