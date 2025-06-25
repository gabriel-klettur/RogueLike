# Resumen de Tests

Este documento describe brevemente lo que comprueba cada test en el proyecto.

| Archivo | Descripción |
|---|---|
| tests/core/test_components.py | Prueba los componentes Position, Velocity, CombatStats, MeleeWeapon y HitboxComponent. |
| tests/core/test_ecs_manager.py | Verifica creación y eliminación de entidades, filtrado por componentes y entidades en cámara. |
| tests/systems/combat/test_melee_combat_system.py | Comprueba el sistema de combate cuerpo a cuerpo: daño en rango, daño no negativo y limpieza de intenciones de ataque. |
| tests/systems/core/test_spawn_debug_system_cache.py | Valida el sistema de depuración de spawn: caché de fuentes y superficies de texto y frustum culling. |
| tests/systems/rendering/test_hitbox_debug_system.py | Comprueba dibujo de arco y círculo de hitbox sin errores y verificación de píxel. |
| tests/systems/rendering/test_hitbox_debug_system_cache.py | Verifica caché de superficies circulares y culling en el sistema de depuración de hitbox. |
| tests/systems/rendering/test_states_debug_render_system_cache.py | Valida caché de etiquetas de estados y culling en el sistema de depuración de estados. |
| tests/test_fsm_integration.py | Test de integración del FSM: ciclo completo de estados Idle -> Patrol -> Aggro -> Attack -> Flee -> Death. |
