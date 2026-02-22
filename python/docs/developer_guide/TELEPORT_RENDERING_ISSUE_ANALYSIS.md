# Análisis: Problema de Renderizado en Teleportación Entre Mapas

## **Resumen del Problema (Summary)**
Al teleportarse entre mapas/mundos, algunas partes del mapa destino no se renderizan correctamente y aparecen tiles del mapa origen (fallback rendering). Esto ocurre debido a una combinación de factores relacionados con el sistema de caché y la política de renderizado overlay-only.

---

## **Arquitectura del Sistema de Teleportación**

### **Flujo de Componentes**
```
TeleportSystem / BuildingPortalSystem
    ↓
MapManager.swap_world_and_spawn(dest_world, tile_pos)
    ↓
WorldService.activate(world_id)  → Actualiza rutas y current_world
    ↓
Limpieza de caches (view, sprites, PKL)
    ↓
MapManager.reload_map()
    ↓
MapLoader.load(map_name)
    ↓
build_map() → MapService.build_map() → TextMapLoader.load()
    ↓
ChunkedMapView._build_chunk_surfaces() + get_sprite_for_tile()
```

---

## **Puntos Críticos Identificados**

### **1. Sistema de Cache Multi-Nivel**

**Caches activos durante teleportación:**

| Cache | Tipo | Ámbito | Limpieza |
|-------|------|--------|----------|
| `Map PKL` | Archivo | Por mundo | `swap_world_and_spawn` L219-222 (solo mundo DESTINO) |
| `_SPRITE_CACHE` | Memoria | Global | `clear_sprite_caches()` L214 |
| `_BASE_TILE_IMAGES_CACHE` | Memoria | Global | `clear_sprite_caches()` L166-168 |
| `_SCALED_CACHE` | Memoria | Global (module) | `view.invalidate_cache()` L239 |
| `chunks_by_zoom` | Memoria | Por vista | `view.invalidate_cache()` L236 |
| `zone_offsets` | Memoria | cached_property | `refresh_zone_offsets()` L343-347 |

**PROBLEMA**: El cache PKL del mundo ORIGEN no se borra, solo el del DESTINO.

### **2. Orden de Operaciones en swap_world_and_spawn**

```python
# Línea 202: Activa mundo destino
world_service.activate(world_id)  # current_world = "world_B"

# Línea 209-214: Limpia caches de rendering
self.view.invalidate_cache()
clear_sprite_caches()

# Línea 219-222: Borra cache PKL del mundo DESTINO
cache_file = Path(...) / f"map_{world_id}_{self.map_name}.pkl"  # world_B
if cache_file.exists():
    cache_file.unlink(missing_ok=True)

# Línea 228: Invalida zone_offsets
global_map_settings.refresh_zone_offsets()

# Línea 233-234: Recrea renderer y vista (nuevos, vacíos)
self.renderer = MapRenderer()
self.view = self.renderer.view

# Línea 244: Recarga mapa
self.reload_map()
```

**PROBLEMA POTENCIAL**: Si `world_service.activate()` falla silenciosamente (try/except L202-204), `current_world` no se actualiza pero el flujo continúa.

### **3. MapLoader.load() - Lógica de Cache**

```python
# Línea 38-42: Determina qué cache buscar
world_id = getattr(global_map_settings, 'current_world', 'base')
cache_file = self.cache_dir / f'map_{world_id}_{map_name}.pkl'

# Línea 85-92: Intenta cargar cache
if cache_file.exists():
    result = pickle.load(f)
    return result  # RETORNA SIN GENERAR MAPA NUEVO
```

**ESCENARIO PROBLEMÁTICO**:
1. Usuario está en `world_A`
2. Teleport a `world_B`
3. `world_service.activate("world_B")` falla → `current_world` sigue siendo `"world_A"`
4. `swap_world_and_spawn` borra: `map_world_B_global_map.pkl`
5. `MapLoader.load()` busca: `map_world_A_global_map.pkl` (¡EXISTE!)
6. Carga mapa de world_A en lugar de world_B

### **4. Map.__setstate__ - Reconstrucción desde PKL**

```python
def __setstate__(self, state):
    self.matrix = state['matrix']
    self.layers = state['layers']  # Códigos de overlay del mundo ORIGEN
    self.metadata = state['metadata']
    self.name = state['name']
    
    # Reconstruye tiles con códigos antiguos pero rutas nuevas
    self.tiles_by_layer = {
        layer: load_tiles_from_text(self.matrix, codes)
        for layer, codes in self.layers.items()
    }
```

**PROBLEMA**: Si el PKL es del mundo origen:
- `layers` contiene códigos de overlay del mundo A
- `load_tiles_from_text` llama a `get_sprite_for_tile(char, code_from_world_A)`
- `get_sprite_for_tile` busca sprites usando `global_map_settings.overlays_dir` → apunta a world_B
- Si `code_from_world_A` no existe en OVERLAY_CODE_MAP de world_B → fallback a sprite base

### **5. Política Overlay-Only en get_sprite_for_tile**

```python
# assets.py L74-119
overlay_only = False

# 1) Preferir is_blank_world()
overlay_only = global_map_settings.is_blank_world()

# 2) Fallback: inspeccionar ZONES_INDEX
z = global_map_settings.ZONES_INDEX  # Puede estar en world_B ahora
if zones_empty:
    overlay_only = True

# 3) Inspeccionar overlays_dir
odir = global_map_settings.overlays_dir  # Puede estar en world_B
files = list(Path(odir).glob('*.overlay.json'))
if not files or only_sentinels:
    overlay_only = True

# L122-124: Si overlay-only y código inválido → return None
if overlay_only and (not overlay_code or overlay_code not in OVERLAY_CODE_MAP):
    return None
```

**PROBLEMA**: Si evaluamos política con rutas de world_B pero códigos de world_A, podemos obtener `None` para sprites válidos.

---

## **Escenarios de Fallo**

### **Escenario A: current_world No Se Actualiza**
```
Estado inicial: current_world = "world_A"
Teleport a "world_B"
→ world_service.activate("world_B") FALLA (excepción silenciada)
→ current_world = "world_A" (sin cambios)
→ Borra cache: map_world_B_global_map.pkl
→ MapLoader busca: map_world_A_global_map.pkl ← EXISTE
→ Carga mapa de world_A con rutas de world_B
→ RESULTADO: Tiles mezclados
```

### **Escenario B: Cache PKL Desactualizado**
```
Estado inicial: current_world = "world_A"
Teleport a "world_B"
→ world_service.activate("world_B") OK
→ current_world = "world_B"
→ Borra cache: map_world_B_global_map.pkl
→ MapLoader busca: map_world_B_global_map.pkl ← NO EXISTE
→ Genera mapa nuevo → OK

PERO... si cache PKL de world_B se regenera entre borrado y carga:
→ MapLoader busca: map_world_B_global_map.pkl ← EXISTE (recién creado)
→ Contiene datos obsoletos o parciales
→ RESULTADO: Tiles incorrectos
```

### **Escenario C: Contaminación de Códigos de Overlay**
```
world_A tiene código overlay: "forest_tile_A_01"
world_B NO tiene ese código en OVERLAY_CODE_MAP
→ PKL de world_A serializa layers con "forest_tile_A_01"
→ Si se deserializa cuando current_world = "world_B":
  → get_sprite_for_tile(".", "forest_tile_A_01")
  → "forest_tile_A_01" not in OVERLAY_CODE_MAP
  → overlay_only = True (world_B podría ser blank)
  → return None
  → load_tiles_from_text usa placeholder transparente
→ RESULTADO: Tiles invisibles o con fallback
```

---

## **Soluciones Propuestas**

### **Solución 1: Invalidar TODOS los Caches PKL en Teleport** ⭐ RECOMENDADA

**Implementación:**
```python
# En swap_world_and_spawn, línea 217-223, reemplazar:
try:
    # Borrar cache del mundo DESTINO
    dest_cache = Path(...) / f"map_{world_id}_{self.map_name}.pkl"
    if dest_cache.exists():
        dest_cache.unlink(missing_ok=True)
    
    # Borrar cache del mundo ORIGEN para evitar contaminación
    try:
        origin_world = getattr(global_map_settings, 'current_world', None)
        if origin_world and origin_world != world_id:
            origin_cache = Path(...) / f"map_{origin_world}_{self.map_name}.pkl"
            if origin_cache.exists():
                origin_cache.unlink(missing_ok=True)
                logger.info(f"[{trace_id}] Cleared origin cache: {origin_cache}")
    except Exception:
        pass
except Exception:
    pass
```

**Ventajas:**
- Elimina cualquier posibilidad de cargar cache del mundo equivocado
- Simple y robusto
- No afecta performance significativamente (los caches se regeneran)

**Desventajas:**
- Aumenta ligeramente el tiempo de teleport (regenera mapa desde cero)

### **Solución 2: Validar current_world Antes de Continuar**

**Implementación:**
```python
# En swap_world_and_spawn, después de línea 204:
try:
    world_service.activate(world_id)
except Exception as e:
    logger.error(f"[{trace_id}] CRITICAL: world_service.activate failed: {e}")
    raise  # NO silenciar este error

# Verificar que current_world se actualizó correctamente
actual_world = getattr(global_map_settings, 'current_world', None)
if actual_world != world_id:
    raise RuntimeError(
        f"World activation failed: expected '{world_id}', got '{actual_world}'"
    )
```

**Ventajas:**
- Detección temprana de fallos críticos
- Previene estados inconsistentes

**Desventajas:**
- Más estricto, podría exponer bugs latentes
- Requiere manejo de errores en el caller

### **Solución 3: Forzar Regeneración de Mapa (Sin Cache)**

**Implementación:**
```python
# En MapLoader.load(), agregar parámetro force_rebuild:
def load(self, map_name: str, force_rebuild: bool = False) -> Any:
    # ... código existente ...
    
    # Saltar cache si force_rebuild
    if force_rebuild:
        logger.info(f"Forcing rebuild for {map_name} (cache bypass)")
        cache_file = Path("nonexistent")  # Forzar a no encontrar cache
    
    if cache_file.exists():
        ...
```

```python
# En reload_map(), pasar force_rebuild después de teleport:
def reload_map(self, force_rebuild: bool = False):
    result = self.loader.load(self.map_name, force_rebuild=force_rebuild)
    ...

# En swap_world_and_spawn:
self.reload_map(force_rebuild=True)
```

**Ventajas:**
- Garantiza mapa fresco después de teleport
- No afecta caches de otros mundos

**Desventajas:**
- Requiere cambios en múltiples firmas de métodos
- Siempre regenera (sin aprovechar cache válido)

### **Solución 4: Agregar Validación en Map.__setstate__**

**Implementación:**
```python
def __setstate__(self, state):
    self.matrix = state['matrix']
    self.layers = state['layers']
    self.metadata = state['metadata']
    self.name = state['name']
    
    # Validar que los códigos de overlay son válidos para el mundo actual
    from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP
    valid_layers = {}
    for layer, codes_grid in self.layers.items():
        # Filtrar códigos inválidos
        valid_grid = [
            [
                code if (not code or code in OVERLAY_CODE_MAP) else ""
                for code in row
            ]
            for row in codes_grid
        ]
        valid_layers[layer] = valid_grid
    
    # Reconstruir tiles con códigos validados
    self.tiles_by_layer = {
        layer: load_tiles_from_text(self.matrix, codes)
        for layer, codes in valid_layers.items()
    }
    self.layers = valid_layers  # Actualizar con códigos limpios
    ...
```

**Ventajas:**
- Defensivo contra códigos obsoletos
- No requiere cambios en el flujo de teleport

**Desventajas:**
- Solo mitiga el síntoma, no la causa raíz
- Podría "limpiar" códigos válidos si OVERLAY_CODE_MAP no está actualizado

---

## **Recomendación Final**

**Implementar Solución 1 + Solución 2**:

1. **Borrar caches de AMBOS mundos** (origen y destino) durante teleport
2. **Validar que `current_world` se actualiza correctamente** antes de continuar
3. **Agregar logging diagnóstico** para rastrear problemas futuros

**Código propuesto:**

Ver siguiente sección para implementación detallada.

---

## **Términos Técnicos (Glosario)**

- **Fallback rendering**: Renderizado de respaldo que ocurre cuando un sprite no se encuentra
- **Cache invalidation**: Proceso de marcar un cache como obsoleto para forzar su recarga
- **Overlay code**: Código que mapea a un asset de tile específico (ej. "forest_01" → "forest_01.png")
- **PKL (Pickle)**: Formato de serialización de Python para objetos complejos
- **cached_property**: Property de Python que se calcula una vez y se cachea hasta invalidación
- **Zone offset**: Coordenadas (x,y) del inicio de una zona dentro del mapa global
- **Overlay-only policy**: Política de renderizado que solo dibuja tiles con códigos de overlay explícitos
- **Race condition**: Situación donde el resultado depende del orden de ejecución de procesos concurrentes

---

**Fecha de análisis**: 2025-01-XX  
**Autor**: Cascade AI Assistant  
**Estado**: Análisis completo - Soluciones propuestas
