# Análisis Exhaustivo de Rendimiento (FPS)

> **Objetivo**: Identificar todos los puntos de caída de FPS en el juego, documentar sus causas raíz, y proporcionar un plan de acción priorizado para alcanzar **60 FPS constantes**.

---

## 1. Arquitectura del Game Loop

```
GameLoop._process_frame()
├── 1. handle_events()          ← ~0.1ms (negligible)
├── 2. update()                 ← update_game(): cámara, editores, buildings
├── 3. render()                 ← Render Pipeline (14 pasos benchmarked)
├── 4. run_ecs_phase()
│   ├── update_ecs()            ← ECS UPDATE: ~80 sistemas secuenciales
│   └── render_ecs()            ← ECS RENDER: ~35 sistemas secuenciales
└── 5. _post_frame()
    ├── pygame.display.flip()
    ├── update_caption()
    ├── autosave_if_needed()
    └── clock.tick(60)          ← FPS cap
```

**Presupuesto por frame a 60 FPS**: `16.67ms` total.

---

## 2. Mapa de Costos por Fase

| Fase | Archivo | Costo estimado | Escala |
|------|---------|---------------|--------|
| **Render Pipeline** | `pipeline_runner.py` | 4-8ms | Fijo + O(buildings + NPCs) |
| **ECS UPDATE** | `manager.py:update()` | 3-12ms | O(N) × ~80 sistemas |
| **ECS RENDER** | `manager.py:render()` | 1-4ms | O(N) × ~35 sistemas |
| **display.flip()** | pygame | 1-3ms | Fijo (GPU sync) |
| **update_game()** | `update_manager.py` | 0.5-2ms | Fijo |

**Total típico**: 10-29ms → **34-100 FPS** dependiendo de la escena.

---

## 3. Puntos Críticos de Caída de FPS

### 3.1 ✅ RESUELTO: Render Pipeline — Object Creation Overhead

**Archivo**: `src/roguelike_game/managers/core/render/entities_renderer.py`

**Problema original**: Se creaba un `_NPCWrapper` por cada NPC visible **cada frame** para poder usar `render_z_ordered()`. Esto implicaba:
- Allocación de objeto Python por NPC
- 3 dict lookups en `__init__`
- Property access overhead en `render()`
- Sorting via `render_z_ordered()` con buckets intermedios

**Solución implementada**: Reemplazado por tuples ligeros `(z_layer, y_pos, render_type, data)` que se ordenan una sola vez y se renderizan con `screen.blit()` directo. Buildings y NPCs se mezclan en una sola lista ordenada.

**Ahorro**: ~0.5-2ms/frame con 100+ NPCs visibles.

> **Nota**: `RenderSystem` (en `render_system.py`) NO está registrado en el system registry, por lo que NO hay doble rendering.

---

### 3.2 ✅ RESUELTO: Z-Ordering — Sorting Unificado

**Archivo**: `src/roguelike_game/managers/core/render/entities_renderer.py`

**Problema original**: El sorting de entidades por Z-layer y Y-position ocurría en múltiples pasadas con estructuras intermedias.

**Solución implementada**: Un solo `sort_list.sort(key=lambda t: (t[0], t[1]))` que ordena buildings + NPCs juntos en una sola pasada. Eliminado `render_z_ordered()` para NPCs (solo se usa para el shortcircuit del Map Editor).

**Ahorro**: ~0.3-0.8ms/frame con 200+ entidades visibles.

---

### 3.3 ✅ PARCIAL: ~80 Sistemas de Update Secuenciales (Benchmark Overhead Eliminado)

**Archivo**: `src/roguelike_game/ecs/core/manager.py`

**Problema original**: Se ejecutan **~80 sistemas** en secuencia cada frame. Además, cada sistema se envolvía con `BenchmarkGroup` que creaba closures y llamaba `inspect.signature` + `bind_partial` **cada frame** (~115 wrappers/frame).

**Solución implementada**: Pre-built benchmark callables en `_build_benchmarked_callables()` — se construyen una vez en `_init_systems()` y se reutilizan cada frame. El wrapper es un simple `time.perf_counter()` + append, sin `inspect` ni `bind_partial`.

**Ahorro**: ~0.5-1ms/frame de overhead puro de benchmarking eliminado.

**Problema restante**: Los ~80 sistemas siguen ejecutándose secuencialmente:

| Categoría | Sistemas | Costo típico |
|-----------|----------|-------------|
| **Spawner** | SpawnerPlacement, SpawnerTrigger, SpawnerRuntime, NpcRespawn, SpawnSystem, SpawnStabilization, TimedDespawn, NpcRestore | ~0.5-2ms |
| **Physics** | MovementCollision, FacingSystem, PlayerFacing | ~1-3ms |
| **Combat/Spells** | MeleeCombat, AutoCast, SpellCasting, Fireball, 20+ spell systems | ~0.5-3ms |
| **Particles** | ParticleSystem, 8 emitter systems | ~0.3-1ms |
| **FSM + Animation** | FSMSystem, AnimationSystem, FlashSystem | ~0.5-2ms |
| **Inventory** | 10 inventory systems | ~0.1-0.5ms |
| **Lighting** | TorchLight, LightingSync | ~0.1-0.5ms |
| **Audio** | DamageSfx, AudioSystem | ~0.1ms |
| **Misc** | Trail, Ribbon, Combo, Experience, Expansion, etc. | ~0.2-0.5ms |

**Impacto**: Incluso si cada sistema toma solo 0.1ms, 80 × 0.1ms = 8ms. Con NPCs en combate, los sistemas de combat/spells escalan y pueden sumar 5-10ms adicionales.

**Overhead del benchmarking**: Cada sistema se wrappea con `BenchmarkGroup` que crea closures y lambdas dinámicamente cada frame. Con 80 sistemas, esto añade ~0.5-1ms de overhead puro.

---

### 3.4 🟠 ALTO: Lighting Pipeline

**Archivos**:
- `src/roguelike_game/managers/core/render/pipeline_runner.py` → `_step_ambient_overlay()`, `_step_point_lights()`

**Problema**:
1. **Ambient overlay**: `get_overlay_surface()` + `screen.blit(overlay, BLEND_RGBA_MULT)` — un blit fullscreen con blending es ~1-2ms.
2. **Point lights**: `compose_lightmap()` + `screen.blit(scaled, BLEND_RGBA_ADD)` — composición de lightmap a resolución reducida + upscale + blit fullscreen ~1-3ms.

**Impacto**: ~2-5ms por frame solo en iluminación. Es un costo **fijo** independiente del número de NPCs.

---

### 3.5 🟠 ALTO: Grayscale Post-Processing (Death State)

**Archivo**: `src/roguelike_game/ecs/systems/rendering/render_system.py` → `apply_grayscale()`

**Problema**: Cuando el jugador muere, se aplica un efecto de escala de grises a toda la pantalla usando NumPy:
- `surfarray.pixels3d()`: lock de surface + array view (~0.2ms)
- LUT luminance computation: ~0.5-1ms
- Writeback a los 3 canales: ~0.3ms
- Con `GRAYSCALE_HALF_RES`: 2× `smoothscale()` adicionales (~1ms cada una)

**Impacto**: ~2-4ms extra durante death state. No afecta gameplay normal, pero causa stuttering al morir.

---

### 3.6 🟠 ALTO: `_NPCWrapper` Object Creation per Frame

**Archivo**: `src/roguelike_game/managers/core/render/npc_render_proxy.py`

**Problema**: Se crea un `_NPCWrapper` por cada NPC visible **cada frame** en `entities_renderer.py:75-87`. Aunque usa `__slots__`, la creación de objetos Python tiene overhead de:
- Allocación de memoria
- `__init__` con 3 dict lookups
- Property access overhead en `render()`

**Impacto**: Con 100 NPCs visibles: ~0.5ms solo en creación de wrappers. Con 500: ~2.5ms.

---

### 3.7 🟡 MEDIO: Sprite Scaling Cache Misses

**Archivos**:
- `render_system.py` → `_scaled_sprite_cache`
- `npc_render_proxy.py` → `_scale_cache`

**Problema**: Cada vez que cambia el frame de animación, el `id(surface)` cambia, invalidando el cache de sprites escalados. Esto fuerza un `pygame.transform.rotozoom()` o `pygame.transform.scale()` que cuesta ~0.1-0.5ms por sprite.

**Impacto**: Con 50 NPCs animándose simultáneamente y zoom ≠ 1.0: ~5-25ms de scaling por frame en el peor caso. Con zoom = 1.0, no hay scaling y el impacto es cero.

---

### 3.8 ✅ RESUELTO: `time.time()` Calls per Entity

**Archivos modificados**:
- `manager.py` — `self._frame_time = time.time()` una vez por frame
- `animation_system.py` — usa `world._frame_time`
- `fsm_system.py` — usa `world._frame_time`
- `auto_cast_system.py` — usa `world._frame_time`
- `movement_collision_system.py` — usa `world._frame_time`

**Solución implementada**: `ECSWorld.update()` cachea `time.time()` como `self._frame_time` una vez por frame. Los sistemas principales usan `getattr(world, '_frame_time', None) or time.time()` como fallback seguro.

**Ahorro**: ~0.3-0.8ms/frame con 200+ NPCs.

---

### 3.9 ✅ RESUELTO: AutoCastSystem — Frustum Culling + Frame Time Cache

**Archivo**: `src/roguelike_game/ecs/systems/ai/auto_cast_system.py`

**Problema original**: Para cada NPC con `AutoCastComponent`, el sistema evaluaba entries, calculaba distancias, y verificaba cooldowns — incluso para NPCs lejos de la cámara.

**Solución implementada**:
1. `get_active_entity_ids(world, camera)` filtra NPCs fuera de la zona activa
2. `world._frame_time` reemplaza `time.time()` para evitar syscalls redundantes

**Ahorro**: ~1-3ms/frame durante combate (NPCs offscreen ya no evalúan autocast).

---

### 3.10 🟡 MEDIO: Combat Spell Systems — Muchos Sistemas Vacíos

**Archivo**: `system_registry.py` líneas 196-204

**Problema**: Se registran ~25 sistemas de spells (ArcaneFlame, Smoke, Puddle, Mine, Wall, Totem, Summon, DoT, MeteorShower, MeteorFall, etc.) que se ejecutan **cada frame** aunque no haya ninguna instancia activa de ese tipo de hechizo.

Cada sistema hace al menos:
```python
def update(self, world, camera=None):
    comps = world.components.get('XComponent', {})
    if not comps:
        return
```

**Impacto**: ~25 × 0.01ms = ~0.25ms de overhead por frame solo en early-returns. Bajo individualmente, pero contribuye al presupuesto total.

---

### 3.11 🟢 BAJO: `display.flip()` — GPU Sync

**Problema**: `pygame.display.flip()` sincroniza con el driver de video. En algunos sistemas puede bloquear 1-3ms esperando al vsync.

**Impacto**: Fijo, no escalable. Contribuye ~1-3ms constantes.

---

### 3.12 🟢 BAJO: Autosave Check per Frame

**Archivo**: `loop_manager.py:57-63`

**Problema**: Cada frame evalúa `time.time()` y compara con `autosave_interval`. Cuando el autosave se dispara, ejecuta `shutdown_manager.shutdown()` que serializa todo el estado del juego a JSON.

**Impacto**: El check es negligible (~0.001ms). El save en sí puede causar un spike de 50-200ms, pero ocurre cada N minutos.

---

## 4. Análisis de Caídas Específicas por Escenario

### 4.1 Caída al Spawnear NPCs

| Causa | Costo | Estado |
|-------|-------|--------|
| `MonsterBuilder.build()` — 20+ componentes | ~2-5ms/NPC | ✅ Mitigado (Spawn Budget: 3/frame) |
| `Surface.copy()` por NPC | ~0.5-1ms/NPC | ✅ Mitigado (Asset Sharing) |
| `pygame.mask.from_surface()` por NPC | ~0.3ms/NPC | ✅ Mitigado (Shared Masks) |
| `build_fsm_for_archetype()` | ~0.1ms/NPC | Cacheable |

### 4.2 Caída Durante Combate (ACTUAL)

| Causa | Costo estimado | Estado |
|-------|---------------|--------|
| **Doble rendering** de NPCs | ~2-6ms | ❌ Sin resolver |
| **AutoCastSystem** evaluaciones | ~1-3ms | ❌ Sin resolver |
| **FireballSystem** collision checks | ~0.5-2ms | Parcial (spatial hash para NPCs) |
| **SpellCastingSystem** processing | ~0.3-1ms | ❌ Sin resolver |
| **HitboxSystem** melee checks | ~0.3-1ms | ❌ Sin resolver |
| **ParticleSystem** + emitters | ~0.5-2ms | ❌ Sin resolver |
| **FSM transitions** (combat states) | ~0.3-1ms | ✅ Mitigado (frustum culling) |
| **Lighting** fullscreen blits | ~2-5ms | ❌ Sin resolver |
| **Sprite scaling** cache misses | ~1-5ms | ❌ Sin resolver |

**Total durante combate**: ~8-26ms → **38-125 FPS** (con spikes a <30 FPS)

### 4.3 Caída con Muchos NPCs en Pantalla

| NPCs visibles | Costo rendering | Costo update | Total estimado | FPS estimado |
|---------------|----------------|-------------|----------------|-------------|
| 10 | ~2ms | ~3ms | ~8ms | ~60 |
| 50 | ~5ms | ~6ms | ~14ms | ~55-60 |
| 100 | ~8ms | ~10ms | ~21ms | ~45 |
| 200 | ~14ms | ~16ms | ~33ms | ~30 |
| 500 | ~30ms | ~35ms | ~68ms | ~15 |

---

## 5. Desglose del Presupuesto de 16.67ms (Target: 60 FPS)

```
┌─────────────────────────────────────────────────────┐
│ PRESUPUESTO: 16.67ms por frame                      │
├─────────────────────────────────────────────────────┤
│ display.flip()          │ ~1.5ms  │ ████            │
│ Render: Map chunks      │ ~1.0ms  │ ███             │
│ Render: Z-entities      │ ~2.0ms  │ ██████          │
│ Render: Lighting        │ ~2.5ms  │ ████████        │
│ Render: ECS overlays    │ ~1.5ms  │ ████            │
│ ECS UPDATE: Physics     │ ~2.0ms  │ ██████          │
│ ECS UPDATE: FSM+Anim    │ ~1.5ms  │ ████            │
│ ECS UPDATE: Combat      │ ~2.0ms  │ ██████          │
│ ECS UPDATE: Misc        │ ~1.5ms  │ ████            │
│ update_game()           │ ~1.0ms  │ ███             │
│ ─────────────────────── │ ─────── │                 │
│ TOTAL                   │ ~16.5ms │ ← justo al      │
│                         │         │   límite        │
└─────────────────────────────────────────────────────┘
```

**Problema**: El presupuesto está al límite en escenas tranquilas. Cualquier actividad adicional (combate, spells, particles) lo excede.

---

## 6. Plan de Acción Priorizado

### Prioridad 1 — Impacto Alto, Esfuerzo Bajo

#### P1.1: Eliminar Doble Rendering de NPCs
- **Archivo**: `entities_renderer.py` + `render_system.py`
- **Acción**: Unificar en un solo paso de rendering. `RenderSystem` ya hace culling + sorting + batched blits, que es más eficiente que `render_z_ordered()` con wrappers individuales.
- **Ahorro estimado**: 2-6ms/frame
- **Riesgo**: Medio — requiere verificar que buildings + NPCs sigan ordenados correctamente por Z.

#### P1.2: Cachear `time.time()` por Frame
- **Archivos**: `animation_system.py`, `fsm_system.py`, `auto_cast_system.py`, todos los sistemas que usan `time.time()`
- **Acción**: Almacenar `world._frame_time = time.time()` una vez en `ECSWorld.update()` y usar `world._frame_time` en todos los sistemas.
- **Ahorro estimado**: 0.3-0.8ms/frame
- **Riesgo**: Bajo

#### P1.3: Early-Exit en Sistemas de Spells Vacíos
- **Archivo**: `system_registry.py`
- **Acción**: Agrupar los ~25 sistemas de spells en un `SpellSystemGroup` que primero verifica si hay componentes activos antes de iterar.
- **Ahorro estimado**: 0.2-0.5ms/frame
- **Riesgo**: Bajo

### Prioridad 2 — Impacto Alto, Esfuerzo Medio

#### P2.1: Pre-Scale Sprite Cache por Zoom Level
- **Archivos**: `render_system.py`, `npc_render_proxy.py`
- **Acción**: Cuando el zoom cambia, pre-escalar todos los frames de animación activos y cachearlos. Evitar re-escalar cada frame cuando cambia la animación.
- **Ahorro estimado**: 1-5ms/frame (con zoom ≠ 1.0)
- **Riesgo**: Medio — requiere invalidación de cache al cambiar zoom.

#### P2.2: Frustum Culling en AutoCastSystem
- **Archivo**: `auto_cast_system.py`
- **Acción**: NPCs fuera de la zona activa de la cámara no necesitan evaluar autocast (el jugador no está cerca).
- **Ahorro estimado**: 1-3ms/frame
- **Riesgo**: Bajo — ya existe `get_active_entity_ids()`.

#### P2.3: Frustum Culling en MovementCollisionSystem
- **Archivo**: `movement_collision_system.py`
- **Acción**: NPCs muy lejos de la cámara pueden saltar collision resolution (no hay interacción posible con el jugador).
- **Ahorro estimado**: 0.5-2ms/frame
- **Riesgo**: Medio — NPCs lejanos podrían atravesar paredes, pero no es visible.

#### P2.4: Optimizar Lighting — Reducir Resolución
- **Archivos**: `pipeline_runner.py`, lighting engine
- **Acción**: Componer lightmap a 1/4 de resolución en lugar de 1/2. El efecto visual es casi idéntico.
- **Ahorro estimado**: 1-2ms/frame
- **Riesgo**: Bajo

### Prioridad 3 — Impacto Medio, Esfuerzo Alto

#### P3.1: Object Pooling para `_NPCWrapper`
- **Archivo**: `npc_render_proxy.py`
- **Acción**: Reutilizar instancias de `_NPCWrapper` en lugar de crear nuevas cada frame.
- **Ahorro estimado**: 0.3-1ms/frame
- **Riesgo**: Bajo

#### P3.2: Eliminar Sorting Redundante
- **Archivos**: `render_z_ordered()`, `RenderSystem`, `entities_renderer.py`
- **Acción**: Si se unifica el rendering (P1.1), el sorting se hace una sola vez.
- **Ahorro estimado**: 0.5-1.5ms/frame
- **Riesgo**: Incluido en P1.1

#### P3.3: Batch Particle Rendering
- **Archivos**: Particle render systems
- **Acción**: Agrupar partículas por tipo y usar `screen.blits()` en batch.
- **Ahorro estimado**: 0.3-1ms/frame
- **Riesgo**: Medio

#### P3.4: Reducir Benchmark Overhead
- **Archivo**: `benchmark.py`, `benchmark_groups.py`
- **Acción**: En modo release, desactivar completamente el wrapping de benchmarks (no crear closures).
- **Ahorro estimado**: 0.5-1ms/frame
- **Riesgo**: Bajo

---

## 7. Optimizaciones Ya Implementadas

### Fase 1 (sesión anterior)

| Capa | Descripción | Archivo | Ahorro |
|------|-------------|---------|--------|
| **Spawn Budget** | MAX_SPAWNS_PER_FRAME=3 | `spawn_system.py` | Elimina spikes de spawn |
| **Asset Sharing** | Surfaces/masks compartidos por tipo | `sprite_loader.py` | ~70% reducción en spawn cost |
| **Spatial Hash** | O(N×K) para NPC-NPC collisions | `spatial_hash.py`, `movement_collision_system.py`, `npc_separation_system.py` | O(N²) → O(N×K) |
| **Frustum Culling** | FSM + Animation throttled offscreen | `frustum_culling.py`, `fsm_system.py`, `animation_system.py` | ~87% reducción para NPCs lejanos |
| **Entities Set** | O(1) add/remove/membership | `manager.py` | O(N) → O(1) en remove |

### Fase 2 (sesión actual)

| Capa | Descripción | Archivo | Ahorro estimado |
|------|-------------|---------|----------------|
| **NPC Render Tuples** | Elimina `_NPCWrapper` per-frame, usa tuples + direct blit | `entities_renderer.py` | ~0.5-2ms/frame |
| **Unified Z-Sort** | Un solo sort para buildings + NPCs juntos | `entities_renderer.py` | ~0.3-0.8ms/frame |
| **Frame Time Cache** | `world._frame_time` cachea `time.time()` una vez por frame | `manager.py`, `animation_system.py`, `fsm_system.py`, `auto_cast_system.py`, `movement_collision_system.py` | ~0.3-0.8ms/frame |
| **Pre-built Benchmarks** | Callables pre-construidos en init, sin `inspect`/closures por frame | `manager.py` | ~0.5-1ms/frame |
| **AutoCast Frustum** | NPCs offscreen no evalúan autocast | `auto_cast_system.py` | ~1-3ms/frame en combate |

---

## 8. Resumen de Impacto Esperado

| Escenario | FPS Actual (est.) | FPS Post-P1 (est.) | FPS Post-P1+P2 (est.) |
|-----------|-------------------|--------------------|-----------------------|
| Idle (pocos NPCs) | ~55-60 | ~60 | ~60 |
| Exploración (20 NPCs) | ~50-58 | ~58-60 | ~60 |
| Combate (10 NPCs + spells) | ~35-50 | ~50-58 | ~58-60 |
| Combate intenso (30+ NPCs) | ~25-40 | ~40-55 | ~55-60 |
| Horda (100+ NPCs) | ~15-30 | ~30-45 | ~45-55 |

---

## 9. Herramientas de Diagnóstico Disponibles

El juego ya incluye un sistema de benchmarking integrado:

- **`BenchmarkGroup`**: Mide cada sistema individualmente. Los resultados se almacenan en `perf_log` con claves como `"5.[UPDATE]FSMSystem"`, `"3.1.a map_render_impl"`, etc.
- **`DiagnosticsOverlay`**: Overlay visual en pantalla (toggle con tecla de debug).
- **`@benchmark` decorator**: Mide funciones individuales.

Para identificar el sistema más costoso en runtime:
```python
# En el game loop, después de cada frame:
for key, times in sorted(perf_log.items()):
    avg = sum(times[-60:]) / min(len(times), 60) * 1000
    if avg > 0.5:  # Solo mostrar > 0.5ms
        print(f"{key}: {avg:.2f}ms")
```

---

## 10. Conclusiones

Los **60 FPS constantes** son alcanzables con las optimizaciones de Prioridad 1 y 2. Los principales culpables son:

1. **Doble rendering de NPCs** (~2-6ms desperdiciados) — el fix más impactante.
2. **Lighting fullscreen blits** (~2-5ms fijos) — reducible con menor resolución.
3. **~80 sistemas secuenciales** con overhead de benchmarking (~1-2ms de overhead puro).
4. **Sprite scaling cache misses** durante animación con zoom ≠ 1.0 (~1-5ms).
5. **AutoCastSystem** sin frustum culling (~1-3ms durante combate).

La combinación de P1.1 (eliminar doble rendering) + P1.2 (cachear time) + P2.2 (frustum culling en AutoCast) debería ser suficiente para mantener 60 FPS en la mayoría de escenarios de combate.
