# Migracion completa: Python (Pygame) -> Unity

Este documento define un plan completo, tecnico y ejecutable para migrar el juego desde `python/` a Unity, con foco en:

1. Paridad funcional (que se juegue igual o mejor).
2. Paridad de datos (mapas, NPCs, inventario, progreso).
3. Paridad de rendimiento (sin regresiones fuertes de FPS/CPU/RAM).
4. Base de arquitectura escalable para nuevas features.

---

## 1) Estado actual del proyecto Python (origen de migracion)

### 1.1 Flujo principal del juego

- Entrada principal: `main.py` inicializa Pygame, crea ventana, crea `Game` y ejecuta el loop principal.
- El loop ejecuta por frame: `handle_events -> update -> render -> ecs(update+render) -> post_frame`.
- Inicializacion por etapas con pipeline modular (stages), util para mapear a bootstrap en Unity.

Referencias:

- `python/src/roguelike_game/main.py`
- `python/src/roguelike_game/managers/core/game.py`
- `python/src/roguelike_game/managers/core/loop_manager.py`
- `python/src/roguelike_game/managers/core/initializer.py`

### 1.2 Arquitectura ECS actual

- `ECSWorld` mantiene:
  - `entities` (set de IDs)
  - `components` (diccionario de stores por tipo)
  - listas ordenadas de `update_systems` y `render_systems`
- Registro central de sistemas en `system_registry.py`.
- Componentes definidos en `component_registry.py`.

Referencias:

- `python/src/roguelike_game/ecs/core/manager.py`
- `python/src/roguelike_game/ecs/core/system_registry.py`
- `python/src/roguelike_game/ecs/core/component_registry.py`

### 1.3 Persistencia y datos

- Config global y paths desde `roguelike_engine/config/config.py`.
- Datos principales en JSON bajo `python/data/` (buildings, particles, lights, inventario, etc).
- Guardado de partida con estado de player, inventario, NPC memory y metadata en `ShutdownManager`.

Referencias:

- `python/src/roguelike_engine/config/config.py`
- `python/src/roguelike_game/managers/core/shutdown_manager.py`

### 1.4 Dependencias relevantes de origen

Basado en `python/requirements.txt`: `pygame-ce`, `tcod`, `pyyaml`, `sqlalchemy`, `alembic`, networking (`websocket-client`, `websockets`, `aiortc`), tooling de test/validacion.

---

## 2) Objetivo de arquitectura en Unity

Se recomienda migrar a **Unity 2D + URP + arquitectura por capas**:

1. **Presentation**: escenas, camara, sprites, tilemaps, VFX, UI.
2. **Gameplay/Core**: reglas de juego, combate, FSM, inventario, spawner.
3. **Data**: ScriptableObjects para catalogos + JSON para runtime/save.
4. **Infrastructure**: carga de assets (Addressables), persistencia, audio, telemetria.

### 2.1 Mapeo Python -> Unity (alto nivel)

1. `GameLoop`/`Game` -> `GameBootstrap` + `GameDirector` (MonoBehaviour persistente).
2. `ECSWorld.components` (dicts) ->
   - Opcion A: Unity DOTS (IComponentData + Systems)
   - Opcion B: ECS custom sobre C# (si quieren menor riesgo inicial)
3. `system_registry` -> `SystemGroups`/orden explicito de update (pre, gameplay, post, render).
4. `SpatialIndex` -> grilla espacial (NativeParallelMultiHashMap o estructura custom en C#).
5. `ShutdownManager` -> `SaveService` + `SaveMigrator` (versionado de schema).

---

## 3) Configuracion recomendada de Unity (base obligatoria)

## 3.1 Version y plantilla

- Unity LTS recomendada: **2022.3.x LTS** (estable para 2D y pipeline maduro).
- Plantilla: **2D (URP)**.

## 3.2 Paquetes a instalar

Minimo recomendado:

1. Input System
2. Cinemachine
3. 2D Tilemap Editor
4. 2D Sprite
5. TextMeshPro
6. Addressables
7. Test Framework
8. (Opcional) Entities + Burst + Collections (si van por DOTS)

## 3.3 Project Settings clave

1. **Player**
   - Active Input Handling: `Input System Package (New)`
   - Scripting Backend: IL2CPP (builds), Mono solo para iteracion si hace falta.
2. **Time**
   - Fixed Timestep: `0.02` (50 Hz) para logica de fisica consistente.
3. **Physics2D**
   - Configurar capas de colision desde el inicio (`Player`, `NPC`, `Projectile`, `World`, `Pickup`, `UIBlocker`).
4. **URP 2D Renderer**
   - Crear renderer 2D dedicado para luces 2D y sorting estable.
5. **Quality**
   - Definir perfiles Desktop (High) y Low-spec (Medium/Low).
6. **Addressables**
   - Configurar grupos `Core`, `UI`, `VFX`, `Audio`, `Maps`.

---

## 4) Estructura de carpetas recomendada en Unity

```text
unity/
  Assets/
    _Project/
      Art/
      Audio/
      Data/
        Catalogs/
        RuntimeJson/
      Prefabs/
        Characters/
        Combat/
        UI/
      Scenes/
        Bootstrap.unity
        MainGameplay.unity
      Scripts/
        Core/
        Gameplay/
          ECS/
            Components/
            Systems/
            World/
          Combat/
          Inventory/
          AI/
        Data/
        Infrastructure/
        UI/
      Settings/
    Tests/
      EditMode/
      PlayMode/
```

---

## 5) Plan de migracion por fases (end-to-end)

## Fase 0 - Discovery y congelacion de baseline

Objetivo: tener una foto exacta del juego Python antes de portar.

Checklist:

1. Congelar commit baseline en Python.
2. Registrar flujos criticos jugables:
   - mover, atacar, castear, lootear, morir, respawn, guardar/cargar.
3. Exportar muestras de datos reales (`data/`) para pruebas en Unity.
4. Definir KPIs baseline:
   - FPS promedio, frame time p95, memoria aproximada, tiempo de carga.

Salida esperada:

- Documento de paridad funcional + set de escenarios de regresion.

## Fase 1 - Bootstrap tecnico en Unity

Objetivo: proyecto Unity reproducible por cualquier dev.

Checklist:

1. Crear proyecto 2D URP en `unity/`.
2. Configurar paquetes minimos.
3. Configurar escenas:
   - `Bootstrap` (carga inicial)
   - `MainGameplay` (juego)
4. Configurar pipeline de logs y profiling (`ProfilerMarker`, logs con categorias).
5. Configurar asmdefs por dominio:
   - `Core`, `Gameplay`, `Infrastructure`, `UI`, `Tests`.

## Fase 2 - Contratos de datos y migradores

Objetivo: que Unity pueda leer los datos actuales del Python.

Checklist:

1. Inventariar JSONs de `python/data`.
2. Definir `schema_version` por cada tipo de archivo.
3. Crear convertidores C# para cada bloque:
   - Buildings templates/instances
   - Particles instances
   - Lights presets/instances
   - Inventario y drops
   - Save metadata / npc memory
4. Implementar validacion estricta (fallar rapido si schema invalido).

Regla:

- Nunca acoplar gameplay directo a JSON crudo. Siempre pasar por DTO + mapper.

## Fase 3 - Vertical slice jugable minimo

Objetivo: obtener un slice pequeno pero jugable en Unity.

Alcance minimo:

1. Player movement + colision de mundo.
2. Camara de seguimiento.
3. Tilemap y sorting basico por Y/Z.
4. 1 NPC con FSM simple (Idle/Chase/Attack).
5. 1 hechizo/projectil.
6. Guardar/cargar posicion + HP + inventario basico.

Condicion de salida:

- Se puede jugar 5-10 minutos sin errores bloqueantes.

## Fase 4 - Migracion completa de ECS y sistemas

Objetivo: portar progresivamente sistemas del registro Python.

Estrategia:

1. Migrar por dominios funcionales, no por archivo suelto:
   - Input y movimiento
   - Combate y spells
   - IA/FSM
   - Inventario y pickups
   - Spawner/runtime
   - Render overlays y HUD
2. Mantener orden de ejecucion equivalente al Python.
3. Para cada sistema migrado:
   - test unitario del comportamiento
   - test de integracion en PlayMode

## Fase 5 - UI, herramientas y editores

Objetivo: sustituir tooling in-game/editor actual por tooling Unity.

1. Convertir overlays criticos a UI Toolkit/UGUI.
2. Crear EditorWindows para tareas de autoria:
   - spawn points
   - templates de NPC
   - tuning de spells
3. Mantener separacion runtime vs editor (`#if UNITY_EDITOR`).

## Fase 6 - Persistencia final y compatibilidad de saves

Objetivo: no perder progreso de jugadores al migrar.

1. Definir `SaveFile v2` (Unity) con migrador desde formato Python.
2. Guardar en `Application.persistentDataPath`.
3. Backups rotativos (`save_1.bak`, `save_2.bak`).
4. Verificacion post-load (integridad de entidades clave).

## Fase 7 - Rendimiento, QA y hardening

Objetivo: estabilizar para release.

1. Profile CPU/GPU/memoria en escenas objetivo.
2. Revisar hotspots:
   - loops de entidades
   - instanciacion de prefabs
   - GC allocations por frame
3. Aplicar pooling (proyectiles, VFX, drops, mobs).
4. Culling y throttling de updates offscreen.
5. Pruebas soak (30-60 min) sin leaks ni degradacion severa.

## Fase 8 - Build y release

Objetivo: pipeline de build estable.

1. Targets iniciales: Windows x64 (primero), luego Linux/macOS si aplica.
2. Pipeline CI:
   - compilacion
   - EditMode tests
   - PlayMode smoke tests
3. Versionado semantico y notas de migracion para usuarios.

---

## 6) Mapeo detallado de modulos (Python -> Unity)

1. **Core loop**
   - Python: `Game`, `GameLoop`, `update_manager`, `events`
   - Unity: `GameDirector`, `InputRouter`, `SimulationOrchestrator`

2. **ECS World**
   - Python: `ECSWorld` con stores por componente
   - Unity: `WorldState` + sistemas por dominio (o DOTS groups)

3. **System registry**
   - Python: orden manual en `get_update_system_classes/get_render_system_classes`
   - Unity: orden explicito con grupos (`PreSimulation`, `Simulation`, `PostSimulation`, `Presentation`)

4. **Data config/path**
   - Python: `config.py` con rutas `assets/`, `data/`
   - Unity: `ScriptableObject` para config + `Addressables` + `persistentDataPath`

5. **Save/Shutdown**
   - Python: `ShutdownManager`
   - Unity: `SaveService` + autosave timer + eventos de aplicacion (`OnApplicationPause/OnApplicationQuit`)

---

## 7) Estrategia de pruebas para asegurar paridad

## 7.1 Tipos de pruebas

1. **Unitarias (EditMode):** reglas de combate, cooldowns, calculos de daño, validacion de DTOs.
2. **Integracion (PlayMode):** input->movimiento->colision->combate->loot.
3. **Golden tests de datos:** mismo input JSON -> mismo estado esperado.
4. **Smoke de scene boot:** cargar escena principal sin errores ni referencias null.

## 7.2 Criterios de aceptacion

Una feature se considera migrada si cumple:

1. Equivalencia funcional visible.
2. Tests minimos pasando.
3. Sin regresion de rendimiento significativa en escenario comparable.
4. Documentacion de uso/mantenimiento actualizada.

---

## 8) Riesgos principales y mitigacion

1. **Riesgo:** migrar todo de una vez.
   - **Mitigacion:** vertical slices y entregas incrementales.

2. **Riesgo:** romper saves existentes.
   - **Mitigacion:** migradores versionados + backups + validaciones.

3. **Riesgo:** degradacion de rendimiento por GC/instanciacion.
   - **Mitigacion:** object pooling + profiling temprano + budget por frame.

4. **Riesgo:** acoplamiento fuerte entre UI y gameplay.
   - **Mitigacion:** arquitectura por capas + eventos + servicios.

---

## 9) Definicion de "migracion completa"

Se considera completada cuando:

1. El juego principal corre en Unity con paridad de loops core.
2. Sistemas criticos (movimiento, colision, combate, IA/FSM, inventario, spawner, save/load) estan portados.
3. Datos existentes de Python son importables o migrables sin perdida relevante.
4. Hay tests automatizados minimos y pipeline de build.
5. Hay guia operativa para desarrollo y release del cliente Unity.

---

## 10) Proximo paso inmediato (accionable)

1. Crear el proyecto Unity 2D URP en esta carpeta `unity/`.
2. Implementar `Bootstrap.unity` + `MainGameplay.unity`.
3. Portar primero el **vertical slice minimo** (Fase 3).
4. Validar paridad con una lista corta de escenarios de juego reales.

Cuando esto este listo, actualizamos este README con:

- estado por fase,
- decisiones de arquitectura tomadas,
- debt tecnico pendiente,
- checklist de release.
