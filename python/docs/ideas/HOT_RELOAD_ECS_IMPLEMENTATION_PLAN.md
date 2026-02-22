# Hot-Reload de Sistemas ECS en RogueLike

## 1. Propósito y Alcance
Este documento describe el concepto de _hot-reload_ aplicado a un motor ECS (Entity–Component–System) y presenta un plan de alto nivel para refactorizar RogueLike de modo que:

- Permita recargar módulos o reinstanciar sistemas en caliente.
- Mejore la modularidad, testabilidad y soporte de plugins.

## 2. ¿Qué es el Hot-Reload en ECS?
Hot-reload es la capacidad de:

1. Detectar cambios en los archivos de código.
2. Volver a cargar dinámicamente clases, funciones o sistemas sin detener todo el proceso.
3. Reemplazar implementaciones antiguas por nuevas en tiempo de ejecución.

## 3. Beneficios

- **Iteración ultrarrápida**: prueba cambios de lógica al vuelo.
- **Modularidad**: los sistemas se gestionan vía _factory_/registry en lugar de estar hard-codeados.
- **Testabilidad**: inyección sencilla de mocks y reinicio de estado por sistema.
- **Extensibilidad**: carga de plugins o mods sin recompilar el engine.

## 4. Arquitectura Propuesta

1. **Registry dinámico**
   - Gestor central que mapea nombres a clases de sistemas.
   - API: `register_system(name, cls)`, `unregister_system(name)`, `get_system(name)`.
2. **Factory de instancias**
   - Cada frame (o en punto configurable) el loop consulta el registry y crea nuevas instancias.
   - Aísla el estado de cada sistema.
3. **Módulo de recarga**
   - Funciones `reload_module(path)` y `reload_system(name)` basadas en `importlib.reload()`.
   - Opción alternativa: restart total del subproceso de juego para cambios masivos.
4. **Watcher o Trigger**
   - Opcional: uso de `watchdog` para detección automática de cambios.
   - Alternativa: consola debug o endpoint HTTP que invoque recarga/manual restart.

## 5. Plan de Implementación de Alto Nivel

1. Refactorizar `ecs/core/manager.py`:
   - Sustituir lista estática de sistemas por un registry dinámico.
   - Extraer el registro en `ecs/core/registry.py`.
2. Implementar **Factory**:
   - En el bucle principal, iterar sobre `registry.list()` y generar instancias.
3. Crear módulo **ReloadManager**:
   - (`reload_module(path)`, `reload_system(name)`).
4. Tests unitarios:
   - Extender `test_spawn_debug_system_cache.py` para inyectar sistemas en el registry.
   - Verificar que tras `reload_system` se usa la nueva definición.
5. Integración de `watchdog` (opcional):
   - Añadir script `dev_reload.py` que combine watcher + loop principal.
6. Documentación y Scripts:
   - Actualizar README con instrucciones de desarrollo y ejecución en modo hot-reload.

## 6. Próximos Pasos

- Revisar y acordar la API del registry y factory.
- Definir convención de nombres de módulos y sistemas.
- Planificar integración en CI para validar recarga automática en tests.
