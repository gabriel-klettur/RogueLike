# FSM para NPCs – Implementation Plan

Este documento describe las 10 fases propuestas para implementar la Máquina de Estados Finitos (FSM) de los NPCs en  
`src/roguelike_game/ecs`.

---

## Fase 1: Definición de requisitos y alcance (✅ Completada)
**Objetivo:** Establecer comportamientos y datos necesarios para la FSM.

Tareas:
- Extraer estados y variables de `design.md` (Patrol, Chase, Attack, Dodge, Flee, Dead; DistanciaJugador, VidaActual, Umbrales, Probabilidades).
- Crear componente `AIConfig` o actualizar `monsters.json` con parámetros: `detectionRange`, `attackRange`, `fleeThreshold`, `fleeChance`, `dodgeChance` en caso de que no esten implementados.
- Documentar en `docs/specs/fsm_npcs_requirements.md` tablas de sensores y valores por monstruo.
- Definir métricas de rendimiento (NPCs simultáneos, coste de CPU).

Entregables:
1. `monsters.json` o `AIConfig` con valores iniciales.
2. `docs/specs/fsm_npcs_requirements.md`.

## Fase 2: Diseño del diagrama de estados (✅ Completada)
**Objetivo:** Visualizar estados y transiciones con condiciones claras.

Tareas:
- Crear archivo `docs/diagrams/fsm_npcs.mmd` usando Mermaid: representar nodos y flechas con condiciones (`DistanciaJugador <= rangoDeteccion`, etc.).
- Enumerar eventos de entrada/salida: `OnEnter`, `OnExit`, `OnPlayerDetected`, `OnOutOfRange`, `OnLowHealth`, `OnDeathTimerExpired`.
- Exportar diagrama a SVG/PNG para referencia en la documentación.

Entregables:
- `docs/diagrams/fsm_npcs.mmd` y `fsm_npcs.svg`.

## Fase 3: Núcleo FSM (✅ Completada)
**Objetivo:** Implementar clases base para estados y máquina.

Tareas:
- En `src/roguelike_game/ecs/fsm/state.py`, crear clase abstracta `State` con métodos:
  - `enter(self, entity)`, `execute(self, entity, dt)`, `exit(self, entity)`.
- En `src/roguelike_game/ecs/fsm/fsm.py`, implementar `FiniteStateMachine`:
  - Almacena `current_state`, maneja `change_state(next_state)` llamando a `exit` y `enter`.
  - Método `update(entity, dt)` invoca `execute` del estado activo.
- Escribir pruebas unitarias en `tests/test_fsm_core.py`.

Entregables:
- Módulos `state.py`, `fsm.py` con docstrings y tests.

## Fase 4: Componente FSM (✅ Completada)
**Objetivo:** Integrar FSM con ECS.

Tareas:
- Crear componente `NPCState` en `src/roguelike_game/ecs/components/fsm/npc_state.py`:
  ```python
  @dataclass
  class NPCState:
      fsm: FiniteStateMachine
      current: str
  ```
- En `entity_factory.py`, al crear NPCs, instanciar `NPCState` con FSM inicial (por ejemplo `PatrolState`).
- Añadir `FSMSystem` en `src/roguelike_game/ecs/systems/fsm_system.py` para iterar entidades con `NPCState`, llamando a `fsm.update`.

Entregables:
- `npc_state.py`, `fsm_system.py` y actualización en `entity_factory.py`.

## Fase 5: Estado Idle (✅ Completada)
**Objetivo:** Comportamiento pasivo y transición a Chase.

Tareas:
- Implementar `IdleState` en `src/roguelike_game/ecs/fsm/states/idle_state.py`:
  - `enter`: registrar timestamp.
  - `execute`: comprobar `DistanciaJugador <= rangoDeteccion` y cambiar a `ChaseState`.
  - `exit`: limpiar animación idle.
- Definir animaciones y tiempo mínimo idle en `AIConfig`.

Entregables:
- `idle_state.py` con lógica y tests en `tests/test_idle_state.py`.

## Fase 6: Estado Patrol (✅ Completada)
**Objetivo:** Recorrer waypoints definidos.

Tareas:
- Crear componente `PatrolRoute` con lista de waypoints (`x,y`).
- Implementar `PatrolState` en `fsm/states/patrol_state.py`:
  - `enter`: cargar ruta.
  - `execute`: mover NPC hacia siguiente waypoint usando sistema de navegación.
  - Transición a `IdleState` si ruta completada o a `ChaseState` si detecta jugador.
- Ajustar `entity_factory.py` para asignar rutas a NPCs.

Entregables:
- `patrol_state.py`, `patrol_route.py`, y tests de ruta.

## Fase 7: Estado Aggro (✅ Completada)
**Objetivo:** Persecución activa del jugador.

Tareas:
- Implementar `AggroState` en `fsm/states/aggro_state.py`:
  - `execute`: llamar a `AggroSystem.track_target(entity, player_pos)`.
  - Comprobar `DistanciaJugador <= rangoAtaque` para cambiar a `AttackState`.
  - Comprobar salud para cambio a `FleeState`.
- Reutilizar y extender `AggroSystem` en `src/roguelike_game/ecs/systems/ai/aggro_system.py`.

Entregables:
- `aggro_state.py` y actualización de `aggro_system.py`.

## Fase 8: Estado Attack (En progreso)
**Objetivo:** Ejecución de lógica de combate.

Tareas:
- Implementar `AttackState` en `fsm/states/attack_state.py`:
  - `execute`: invocar `CombatSystem.perform_melee(entity, target)`.
  - Gestionar cooldown y animaciones.
- Verificar distancia para volver a `AggroState` o `ChaseState`.
- Ajustar `CombatSystem` en `src/roguelike_game/ecs/systems/combat_system.py`.

Entregables:
- `attack_state.py`

## Fase 9: Estado Flee y Muerte
**Objetivo:** Comportamientos de huida y muerte.

Tareas:
- Implementar `FleeState` en `fsm/states/flee_state.py`:
  - `execute`: calcular ruta de escape (p.e. alejamiento aleatorio) y aplicar movimiento.
  - Regresar a `PatrolState` si `DistanciaJugador > rangoDeteccion`.
- Implementar `DeathState` en `fsm/states/death_state.py`:
  - `enter`: añadir componente `DeathTimer`.
  - `execute`: esperar expiración de `DeathTimer` y luego remover entidad.
- Reutilizar `DeathSystem` para procesar `DeathTimer`.

Entregables:
- `flee_state.py`, `death_state.py`, y tests de expirar muerte.

## Fase 10: Integración, pruebas y optimización
**Objetivo:** Completar integración, validar y perfilar.

Tareas:
- Registrar `FSMSystem` en `world.py` tras `CombatSystem` y `DeathSystem`.
- Crear tests de integración en `tests/test_fsm_integration.py`: ciclo completo de estados.
- Generar benchmarks de CPU con múltiples NPCs.
- Ajustar parámetros de `AIConfig` y optimizar sistemas lentos.
- Documentar API de FSM y ejemplos de uso en `README.md`.

Entregables:
- `world.py` actualizado, suite de tests completa, resultados de benchmark, guía de usuario.

---