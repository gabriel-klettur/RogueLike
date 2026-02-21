# Sistema de Caché (Cache Manager)

Este paquete centraliza la lógica de cache para el motor **roguelike_engine**. Permite registrar distintos backends (memoria, disco, Redis, etc.) y acceder a ellos de forma uniforme.

## Estructura de Archivos

- **icache.py**: Interfaz genérica `ICache` con métodos `get`, `put`, `invalidate` y `clear`.
- **memory_cache.py**: Implementación `MemoryCache` con política LRU y TTL opcional.
- **file_cache.py**: Implementación `FileCache` que serializa objetos en disco usando `pickle` y TTL.
- **cache_manager.py**: Clase `CacheManager` para registrar y administrar múltiples caches por namespace.

## Instalación

No se requieren dependencias externas adicionales.

```bash
# Asegúrate de que el paquete roguelike_engine esté en tu PYTHONPATH
pm install -e .
```

## Uso Básico

1. Importar y crear el `CacheManager`:

   ```python
   from roguelike_engine.cache.cache_manager import CacheManager
   from roguelike_engine.cache.memory_cache import MemoryCache
   from roguelike_engine.cache.file_cache import FileCache

   cm = CacheManager()
   # Registrar un cache en memoria (máximo 100 entradas)
   cm.register('sprites', MemoryCache(max_size=100))
   # Registrar un cache en disco (TTL 300 segundos)
   cm.register('maps', FileCache(dir_path='cache/maps', ttl=300))
   ```

2. Obtener y usar un cache en tu aplicación:

   ```python
   cache = cm.get_cache('maps')
   key = 'dungeon_01'
   data = cache.get(key)
   if data is None:
       data = build_map('dungeon_01')  # función de generación de mapa
       cache.put(key, data)
   # ... usar data ...
   ```

3. Invalidar o limpiar:

   ```python
   cm.invalidate('sprites', 'hero_sprite')  # invalida sólo esa clave
   cm.clear('maps')                        # elimina todos los mapas de cache
   ```

## Extensión y Buenas Prácticas

- Para añadir nuevos backends (p.ej. Redis), crea una clase que implemente `ICache` y registra en `CacheManager`.
- Usa patrones **cache-aside** o **write-through** según tus necesidades.
- Monitorea `hit/miss` y latencias para ajustar TTL y tamaños.

---

_Este README cubre la configuración y uso inicial del sistema de cache. Para casos de uso avanzados, consulta la documentación adicional o los tests en el directorio `tests/`._