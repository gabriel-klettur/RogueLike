# Plan de Mejora de Rendimiento - RogueLike
**Fecha:** 3 de Diciembre de 2025  
**Objetivo:** Identificar y optimizar los puntos críticos de rendimiento, especialmente durante spawning de NPCs y lanzamiento de proyectiles (fireballs).

---

## Resumen Ejecutivo

Tras analizar el código fuente, se identificaron **caídas de FPS** en dos escenarios principales:
1. **Spawning de NPCs** - Creación masiva de entidades con carga de assets
2. **Lanzamiento de Fireballs** - Detección de colisiones y emisión de partículas

Este documento detalla los puntos críticos y propone optimizaciones ordenadas por impacto estimado.

---

## 🔴 PRIORIDAD ALTA - Impacto Crítico en FPS

### 1. Sistema de Spawning (`spawner_wave.py`, `builder.py`)

#### Problema Identificado
Cuando se spawnean múltiples NPCs simultáneamente (oleadas), se ejecutan operaciones costosas **por cada entidad**:

```python
# builder.py línea 49
_load_caches_once()  # Se llama por CADA monstruo spawneado
```

**Impacto:** La función `_load_caches_once()` verifica y potencialmente carga todos los sprites de monstruos. Aunque tiene un guard, el overhead de verificación se acumula.

#### Puntos Críticos en `spawner_wave.py`:
- **Línea 297:** `npc_tiles = caches.collect_npc_tiles(world)` - Se recalcula por cada spawner activo
- **Línea 331-344:** `choose_spawn_tile()` - Búsqueda O(n) con múltiples verificaciones de colisión
- **Línea 349:** `world.create_entity()` - Creación de entidad sin pooling

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Object Pooling para entidades** | Media | ⭐⭐⭐⭐⭐ |
| **Batch spawning** - Agrupar creación de múltiples NPCs | Media | ⭐⭐⭐⭐ |
| **Lazy loading de sprites** - Cargar solo al primer uso | Baja | ⭐⭐⭐ |
| **Spatial hashing para `choose_spawn_tile`** | Alta | ⭐⭐⭐⭐ |

---

### 2. Sistema de Colisiones de Fireball (`units_detection.py`, `fireball_system.py`)

#### Problema Identificado
La detección de colisiones itera sobre **TODAS las entidades** con `Position`, `MultiCollider`, `Health`:

```python
# units_detection.py línea 24
for target in world.get_entities_with("Position", "MultiCollider", "Health"):
```

**Impacto:** Complejidad O(F × E) donde F = fireballs activos, E = entidades con salud.

#### Puntos Críticos:
- **Línea 35-39:** Triple fallback de detección (mask → circle → rect) por cada entidad
- **Línea 56-72:** `_path_overlaps_entity` crea y une múltiples `pygame.Rect` por frame
- **`precompute_wall_cache`** en `walls.py` - Se recalcula cada frame para TODOS los muros

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Spatial partitioning (Quadtree/Grid)** para colisiones | Alta | ⭐⭐⭐⭐⭐ |
| **Broad-phase AABB culling** antes de mask checks | Baja | ⭐⭐⭐⭐ |
| **Cache de wall data** entre frames (TTL-based) | Baja | ⭐⭐⭐ |
| **Batch collision queries** para múltiples fireballs | Media | ⭐⭐⭐ |

---

### 3. Sistema de Partículas (`fireball_trail_emitter_system.py`, `particle_system.py`)

#### Problema Identificado
Cada fireball emite **2-8 partículas por frame**, creando entidades nuevas:

```python
# fireball_trail_emitter_system.py línea 260
peid = world.create_entity()
comps.setdefault("Position", {})[peid] = Position(...)
comps.setdefault("ParticleComponent", {})[peid] = ParticleComponent(...)
```

**Impacto:** Con 10 fireballs activos × 4 partículas/frame × 60 FPS = **2400 entidades/segundo** creadas y destruidas.

#### Puntos Críticos:
- **Línea 221-224:** Conteo de partículas activas O(n) por cada fireball
- **Línea 245-275:** Creación de entidad + 2 componentes por partícula
- **`particle_system.py` línea 29-54:** Validación de curvas por cada partícula

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Particle pooling** - Reutilizar entidades de partículas | Media | ⭐⭐⭐⭐⭐ |
| **Batch particle rendering** - Un draw call para todas | Alta | ⭐⭐⭐⭐ |
| **Instanced rendering** con GPU | Alta | ⭐⭐⭐⭐⭐ |
| **Reducir emit_rate dinámicamente** según FPS | Baja | ⭐⭐⭐ |

---

## 🟡 PRIORIDAD MEDIA - Mejoras Significativas

### 4. HitboxSystem (`hitbox_system.py`)

#### Problema Identificado
El sistema crea **superficies y máscaras temporales** cada frame:

```python
# hitbox_system.py línea 81
surf = pygame.Surface((w, h), pygame.SRCALPHA)
# ... dibujo de polígono ...
hitmask = pygame.mask.from_surface(surf)
```

**Impacto:** Allocación de memoria + creación de máscara por cada hitbox activo por frame.

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Cache de máscaras de hitbox** por (radio, ángulo) | Media | ⭐⭐⭐⭐ |
| **Precalcular sectores comunes** (90°, 180°, 360°) | Baja | ⭐⭐⭐ |
| **Usar geometría matemática** en lugar de máscaras | Media | ⭐⭐⭐ |

---

### 5. MovementCollisionSystem (`movement_collision_system.py`)

#### Problema Identificado
Reconstruye diccionarios de colisiones NPC cada frame:

```python
# movement_collision_system.py línea 213-230
npc_feet_rects = {}
npc_feet_circles = {}
for nid in world.get_entities_with('Position', 'MultiCollider'):
    # ... construir rect/circle para cada NPC ...
```

**Impacto:** O(N) construcción de estructuras + O(N²) verificaciones de colisión NPC-NPC.

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Cache de colliders NPC** con invalidación selectiva | Media | ⭐⭐⭐⭐ |
| **Spatial hash para NPC-NPC** | Alta | ⭐⭐⭐⭐ |
| **Skip collision para NPCs estáticos** | Baja | ⭐⭐ |

---

### 6. AnimationSystem (`animation_system.py`)

#### Problema Identificado
Reconstruye máscaras de colisión desde sprites cada frame:

```python
# animation_system.py línea 87-96
if cached is None:
    if scale and scale != 1.0:
        scaled_surf = pygame.transform.scale(surf, ...)
    mask = pygame.mask.from_surface(scaled_surf)
    _mask_cache[key] = mask
```

**Impacto:** Aunque hay cache, la key incluye `id(surf)` que cambia con cada frame de animación.

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Precalcular máscaras por animación** en carga | Media | ⭐⭐⭐⭐ |
| **Cache por (animation_state, frame_idx, scale)** | Baja | ⭐⭐⭐ |

---

### 7. RenderSystem (`render_system.py`)

#### Problema Identificado
El cache de sprites escalados usa `id(surface)` como key:

```python
# render_system.py línea 142
key = (eid, id(orig_surface), scale_factor)
```

**Impacto:** Cada cambio de frame de animación invalida el cache, forzando re-escalado.

#### Optimizaciones Propuestas:

| Optimización | Complejidad | Impacto Estimado |
|--------------|-------------|------------------|
| **Pre-escalar sprites en carga** para zooms comunes | Media | ⭐⭐⭐ |
| **Cache por contenido hash** en lugar de id() | Media | ⭐⭐⭐ |
| **Dirty flag** para evitar re-sort cada frame | Baja | ⭐⭐ |

---

## 🟢 PRIORIDAD BAJA - Optimizaciones Menores

### 8. SpawnCaches (`spawner_cache.py`)

#### Estado Actual
Ya implementa TTL-based caching (6 frames para blocked tiles).

#### Mejora Propuesta
- Aumentar TTL a 10-15 frames para reducir recálculos
- Invalidar selectivamente solo cuando cambia el mapa

---

### 9. SpatialIndex (`spatial_index.py`)

#### Estado Actual
Índice estático por celdas de tamaño TILE_SIZE.

#### Mejora Propuesta
- Implementar **dirty regions** para reconstrucción parcial
- Cache de queries frecuentes (área de cámara)

---

### 10. Monster Cache (`cache.py`)

#### Estado Actual
Carga todos los sprites de monstruos al inicio.

#### Mejora Propuesta
- **Lazy loading** por tipo de monstruo
- **Unload** de sprites no usados en X segundos

---

## 📊 Arquitectura de Optimización Propuesta

### Fase 1: Quick Wins (1-2 días)
1. ✅ Implementar broad-phase AABB culling en FireballSystem
2. ✅ Cache de máscaras de hitbox por (radio, ángulo)
3. ✅ Reducir emit_rate de partículas dinámicamente según FPS

### Fase 2: Pooling (3-5 días)
1. 🔄 Object pool para partículas
2. 🔄 Object pool para entidades temporales (hitboxes, efectos)
3. 🔄 Batch spawning de NPCs

### Fase 3: Spatial Optimization (1-2 semanas)
1. 📋 Quadtree/Grid para colisiones de proyectiles
2. 📋 Spatial hash para NPC-NPC collision
3. 📋 Dirty regions para SpatialIndex

### Fase 4: Rendering (2-3 semanas)
1. 📋 Instanced particle rendering
2. 📋 Pre-escalado de sprites
3. 📋 Sprite batching por textura

---

## 🔧 Herramientas de Profiling Recomendadas

1. **cProfile + snakeviz** - Para identificar funciones lentas
2. **py-spy** - Sampling profiler sin overhead
3. **pygame.time.Clock.get_fps()** - Monitoreo en tiempo real
4. **Benchmark decorators existentes** - Ya implementados en el proyecto

### Comando de Profiling Sugerido:
```bash
python -m cProfile -o profile.stats -m roguelike_game.main
snakeviz profile.stats
```

---

## 📈 Métricas de Éxito

| Escenario | FPS Actual (estimado) | FPS Objetivo |
|-----------|----------------------|--------------|
| Idle (sin combate) | 60 | 60 |
| 10 NPCs en pantalla | 55 | 60 |
| Oleada de 20 NPCs | 40 | 55+ |
| 5 Fireballs activos | 50 | 60 |
| Combate intenso (20 NPCs + 10 proyectiles) | 30 | 50+ |

---

## Notas Adicionales

### Patrones Identificados en el Código

1. **Uso extensivo de `getattr` con fallbacks** - Overhead menor pero acumulativo
2. **Creación de listas/dicts temporales** en loops calientes
3. **Logging en paths críticos** - Considerar guards `if __debug__`
4. **Try/except genéricos** - Pueden ocultar errores de rendimiento

### Dependencias Críticas

- `pygame.mask.from_surface()` - Operación costosa, cachear siempre
- `pygame.transform.scale/rotozoom()` - Precalcular cuando sea posible
- `world.get_entities_with()` - Considerar índices secundarios

---

## 🛠️ Ejemplos de Implementación

### Ejemplo 1: Object Pool para Partículas

```python
# src/roguelike_game/ecs/utils/particle_pool.py
class ParticlePool:
    """Pool reutilizable de entidades de partículas."""
    
    def __init__(self, world, initial_size: int = 100):
        self.world = world
        self._available: list[int] = []
        self._in_use: set[int] = set()
        self._preallocate(initial_size)
    
    def _preallocate(self, count: int) -> None:
        for _ in range(count):
            eid = self.world.create_entity()
            self._available.append(eid)
    
    def acquire(self) -> int:
        if not self._available:
            self._preallocate(50)  # Expandir si es necesario
        eid = self._available.pop()
        self._in_use.add(eid)
        return eid
    
    def release(self, eid: int) -> None:
        if eid in self._in_use:
            self._in_use.discard(eid)
            # Limpiar componentes pero mantener entidad
            for comp_dict in self.world.components.values():
                if isinstance(comp_dict, dict):
                    comp_dict.pop(eid, None)
            self._available.append(eid)
```

### Ejemplo 2: Spatial Hash para Colisiones

```python
# src/roguelike_game/ecs/utils/spatial_hash.py
class SpatialHash:
    """Grid espacial para queries de colisión O(1) amortizado."""
    
    def __init__(self, cell_size: int = 64):
        self.cell_size = cell_size
        self._cells: dict[tuple[int, int], set[int]] = {}
    
    def _cell_key(self, x: float, y: float) -> tuple[int, int]:
        return int(x // self.cell_size), int(y // self.cell_size)
    
    def insert(self, eid: int, x: float, y: float, radius: float = 0) -> None:
        # Insertar en todas las celdas que cubre el AABB
        x1, y1 = self._cell_key(x - radius, y - radius)
        x2, y2 = self._cell_key(x + radius, y + radius)
        for cx in range(x1, x2 + 1):
            for cy in range(y1, y2 + 1):
                self._cells.setdefault((cx, cy), set()).add(eid)
    
    def query_radius(self, x: float, y: float, radius: float) -> set[int]:
        x1, y1 = self._cell_key(x - radius, y - radius)
        x2, y2 = self._cell_key(x + radius, y + radius)
        result = set()
        for cx in range(x1, x2 + 1):
            for cy in range(y1, y2 + 1):
                result.update(self._cells.get((cx, cy), set()))
        return result
    
    def clear(self) -> None:
        self._cells.clear()
```

### Ejemplo 3: Cache de Máscaras de Hitbox

```python
# src/roguelike_game/ecs/utils/hitbox_mask_cache.py
import pygame
import math

_HITBOX_MASK_CACHE: dict[tuple[int, float, int], pygame.mask.Mask] = {}

def get_arc_mask(radius: int, arc_angle: float, segments: int = 16) -> pygame.mask.Mask:
    """Obtiene máscara de arco desde cache o la genera."""
    key = (radius, round(arc_angle, 2), segments)
    if key in _HITBOX_MASK_CACHE:
        return _HITBOX_MASK_CACHE[key]
    
    # Generar máscara
    size = radius * 2
    surf = pygame.Surface((size, size), pygame.SRCALPHA)
    center = radius
    
    # Dibujar sector (arco centrado en 0°)
    pts = [(center, center)]
    half_arc = arc_angle / 2
    for i in range(segments + 1):
        ang = -half_arc + arc_angle * i / segments
        pts.append((
            center + math.cos(ang) * radius,
            center + math.sin(ang) * radius
        ))
    pygame.draw.polygon(surf, (255, 255, 255), pts)
    
    mask = pygame.mask.from_surface(surf)
    _HITBOX_MASK_CACHE[key] = mask
    return mask
```

### Ejemplo 4: Reducción Dinámica de Partículas

```python
# En fireball_trail_emitter_system.py
def _get_adaptive_emit_rate(base_rate: int, world) -> int:
    """Reduce emit_rate si FPS está bajo."""
    try:
        fps = getattr(world, '_current_fps', 60)
        if fps < 30:
            return max(1, base_rate // 4)
        elif fps < 45:
            return max(1, base_rate // 2)
        return base_rate
    except Exception:
        return base_rate
```

---

## 📁 Archivos Clave para Optimización

| Archivo | Prioridad | Descripción |
|---------|-----------|-------------|
| `ecs/systems/spawner/spawner_wave.py` | 🔴 Alta | Lógica de oleadas |
| `ecs/systems/combat/spells/fireball_system/` | 🔴 Alta | Sistema de proyectiles |
| `ecs/systems/particles/fireball_trail_emitter_system.py` | 🔴 Alta | Emisión de partículas |
| `ecs/systems/combat/hitbox_system.py` | 🟡 Media | Colisiones melee |
| `ecs/systems/physics/movement_collision_system.py` | 🟡 Media | Física de movimiento |
| `ecs/systems/rendering/render_system.py` | 🟡 Media | Renderizado principal |
| `ecs/systems/rendering/animation_system.py` | 🟡 Media | Animaciones |
| `ecs/core/spatial_index.py` | 🟢 Baja | Índice espacial |
| `factories/monster/cache.py` | 🟢 Baja | Cache de sprites |

---

## Historial de Cambios

| Fecha | Cambio | Autor |
|-------|--------|-------|
| 2025-12-03 | Documento inicial - Auditoría completa | Cascade |
| 2025-12-03 | **Implementación Fase 1 completada** - Ver detalles abajo | Cascade |

---

## ✅ Optimizaciones Implementadas (2025-12-03)

### 1. Object Pool para Partículas
- **Archivo:** `src/roguelike_game/ecs/utils/particle_pool.py` (NUEVO)
- **Integrado en:** `fireball_trail_emitter_system.py`, `particle_system.py`
- **Impacto:** Reduce allocations de ~2400 entidades/segundo a reutilización de pool

### 2. Spatial Hash para Colisiones
- **Archivo:** `src/roguelike_game/ecs/utils/spatial_hash.py` (NUEVO)
- **Integrado en:** `units_detection.py` (FireballSystem)
- **Impacto:** Reduce complejidad de O(F×E) a O(F×k) donde k << E

### 3. Cache de Máscaras de Hitbox
- **Archivo:** `src/roguelike_game/ecs/utils/hitbox_mask_cache.py` (NUEVO)
- **Integrado en:** `hitbox_system.py`
- **Impacto:** Evita crear pygame.Surface y pygame.mask cada frame

### 4. Reducción Dinámica de Partículas
- **Integrado en:** `fireball_trail_emitter_system.py`
- **Comportamiento:**
  - FPS < 25: emit_rate ÷ 4
  - FPS < 35: emit_rate ÷ 3
  - FPS < 45: emit_rate ÷ 2

### 5. Cache de Wall Data
- **Integrado en:** `walls.py` (FireballSystem)
- **Impacto:** Reutiliza geometría de muros entre frames

### 6. Contador de Frames en ECSWorld
- **Integrado en:** `manager.py`
- **Uso:** Permite a los caches detectar cambios de frame
