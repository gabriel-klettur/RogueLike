# Test por hacer (500 archivos propuestos)

Este documento recoge una propuesta profesional de 500 archivos de test a crear para asegurar una cobertura amplia, robusta y escalable del proyecto. La lista está basada en el análisis de la estructura actual del código bajo `src/` y de los tests existentes en `tests/`. Se priorizan áreas críticas del gameplay (ECS), motor (engine), UI y editores.

## Metodología (resumen)
- **Auditoría de estructura**: inspección de paquetes y submódulos en `src/` y catálogo de tests existentes en `tests/`.
- **Criterios**: equilibrio entre unit tests, pruebas de integración pequeñas, validación de esquemas/serialización y rutas de error.
- **Patrones de nombre**: `tests/<paquete>/<ruta>/test_<módulo>_<tema>.py` para reflejar el árbol de `src/` y facilitar trazabilidad.
- **Priorización**: primero `roguelike_game` (ECS/sistemas), luego `roguelike_engine`, `roguelike_ui` y huecos en `roguelike_editors`.

## Distribución por áreas (totales)
- **roguelike_game**: 260
- **roguelike_engine**: 160
- **roguelike_ui**: 50
- **roguelike_editors**: 30
- **Total**: 500

---

## Lista de tests propuestos

### 1) roguelike_game (260)

#### 1.1 Componentes ECS (112)
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_defaults.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_validation.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_serialization.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_invariants.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_merging.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_factory.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_edge_cases.py
- tests/roguelike_game/ecs/components/abilities/test_abilities_component_schema_compat.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_defaults.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_validation.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_serialization.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_invariants.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_merging.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_factory.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_edge_cases.py
- tests/roguelike_game/ecs/components/ai/test_ai_component_schema_compat.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_defaults.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_validation.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_serialization.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_invariants.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_merging.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_factory.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_edge_cases.py
- tests/roguelike_game/ecs/components/chat/test_chat_component_schema_compat.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_defaults.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_validation.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_serialization.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_invariants.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_merging.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_factory.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_edge_cases.py
- tests/roguelike_game/ecs/components/combat/test_combat_component_schema_compat.py
- tests/roguelike_game/ecs/components/core/test_core_component_defaults.py
- tests/roguelike_game/ecs/components/core/test_core_component_validation.py
- tests/roguelike_game/ecs/components/core/test_core_component_serialization.py
- tests/roguelike_game/ecs/components/core/test_core_component_invariants.py
- tests/roguelike_game/ecs/components/core/test_core_component_merging.py
- tests/roguelike_game/ecs/components/core/test_core_component_factory.py
- tests/roguelike_game/ecs/components/core/test_core_component_edge_cases.py
- tests/roguelike_game/ecs/components/core/test_core_component_schema_compat.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_defaults.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_validation.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_serialization.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_invariants.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_merging.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_factory.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_edge_cases.py
- tests/roguelike_game/ecs/components/debug/test_debug_component_schema_compat.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_defaults.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_validation.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_serialization.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_invariants.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_merging.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_factory.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_edge_cases.py
- tests/roguelike_game/ecs/components/fsm/test_fsm_component_schema_compat.py
- tests/roguelike_game/ecs/components/items/test_items_component_defaults.py
- tests/roguelike_game/ecs/components/items/test_items_component_validation.py
- tests/roguelike_game/ecs/components/items/test_items_component_serialization.py
- tests/roguelike_game/ecs/components/items/test_items_component_invariants.py
- tests/roguelike_game/ecs/components/items/test_items_component_merging.py
- tests/roguelike_game/ecs/components/items/test_items_component_factory.py
- tests/roguelike_game/ecs/components/items/test_items_component_edge_cases.py
- tests/roguelike_game/ecs/components/items/test_items_component_schema_compat.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_defaults.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_validation.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_serialization.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_invariants.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_merging.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_factory.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_edge_cases.py
- tests/roguelike_game/ecs/components/particles/test_particles_component_schema_compat.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_defaults.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_validation.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_serialization.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_invariants.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_merging.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_factory.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_edge_cases.py
- tests/roguelike_game/ecs/components/physics/test_physics_component_schema_compat.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_defaults.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_validation.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_serialization.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_invariants.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_merging.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_factory.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_edge_cases.py
- tests/roguelike_game/ecs/components/rendering/test_rendering_component_schema_compat.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_defaults.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_validation.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_serialization.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_invariants.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_merging.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_factory.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_edge_cases.py
- tests/roguelike_game/ecs/components/spawn/test_spawn_component_schema_compat.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_defaults.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_validation.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_serialization.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_invariants.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_merging.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_factory.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_edge_cases.py
- tests/roguelike_game/ecs/components/spawner/test_spawner_component_schema_compat.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_defaults.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_validation.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_serialization.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_invariants.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_merging.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_factory.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_edge_cases.py
- tests/roguelike_game/ecs/components/transform/test_transform_component_schema_compat.py

#### 1.2 Sistemas ECS (102)
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_process.py
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_events.py
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_integration_small.py
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_perf_budget.py
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_error_paths.py
- tests/roguelike_game/ecs/systems/abilities/test_abilities_system_config.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_process.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_events.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_integration_small.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_perf_budget.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_error_paths.py
- tests/roguelike_game/ecs/systems/ai/test_ai_system_config.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_process.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_events.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_integration_small.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_perf_budget.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_error_paths.py
- tests/roguelike_game/ecs/systems/audio/test_audio_system_config.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_process.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_events.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_integration_small.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_perf_budget.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_error_paths.py
- tests/roguelike_game/ecs/systems/chat/test_chat_system_config.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_process.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_events.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_integration_small.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_perf_budget.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_error_paths.py
- tests/roguelike_game/ecs/systems/combat/test_combat_system_config.py
- tests/roguelike_game/ecs/systems/core/test_core_system_process.py
- tests/roguelike_game/ecs/systems/core/test_core_system_events.py
- tests/roguelike_game/ecs/systems/core/test_core_system_integration_small.py
- tests/roguelike_game/ecs/systems/core/test_core_system_perf_budget.py
- tests/roguelike_game/ecs/systems/core/test_core_system_error_paths.py
- tests/roguelike_game/ecs/systems/core/test_core_system_config.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_process.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_events.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_integration_small.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_perf_budget.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_error_paths.py
- tests/roguelike_game/ecs/systems/experience/test_experience_system_config.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_process.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_events.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_integration_small.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_perf_budget.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_error_paths.py
- tests/roguelike_game/ecs/systems/fsm/test_fsm_system_config.py
- tests/roguelike_game/ecs/systems/input/test_input_system_process.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/input/test_input_system_events.py
- tests/roguelike_game/ecs/systems/input/test_input_system_integration_small.py
- tests/roguelike_game/ecs/systems/input/test_input_system_perf_budget.py
- tests/roguelike_game/ecs/systems/input/test_input_system_error_paths.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/input/test_input_system_config.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_process.py
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_events.py
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_integration_small.py
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_perf_budget.py
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_error_paths.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/inventory/test_inventory_system_config.py
- tests/roguelike_game/ecs/systems/items/test_items_system_process.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/items/test_items_system_events.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/items/test_items_system_integration_small.py
- tests/roguelike_game/ecs/systems/items/test_items_system_perf_budget.py
- tests/roguelike_game/ecs/systems/items/test_items_system_error_paths.py [COMPLETADO]
- tests/roguelike_game/ecs/systems/items/test_items_system_config.py
- tests/roguelike_game/ecs/systems/map/test_map_system_process.py
- tests/roguelike_game/ecs/systems/map/test_map_system_events.py
- tests/roguelike_game/ecs/systems/map/test_map_system_integration_small.py
- tests/roguelike_game/ecs/systems/map/test_map_system_perf_budget.py
- tests/roguelike_game/ecs/systems/map/test_map_system_error_paths.py
- tests/roguelike_game/ecs/systems/map/test_map_system_config.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_process.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_events.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_integration_small.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_perf_budget.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_error_paths.py
- tests/roguelike_game/ecs/systems/particles/test_particles_system_config.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_process.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_events.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_integration_small.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_perf_budget.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_error_paths.py
- tests/roguelike_game/ecs/systems/physics/test_physics_system_config.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_process.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_events.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_integration_small.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_perf_budget.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_error_paths.py
- tests/roguelike_game/ecs/systems/rendering/test_rendering_system_config.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_process.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_events.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_integration_small.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_perf_budget.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_error_paths.py
- tests/roguelike_game/ecs/systems/spawner/test_spawner_system_config.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_process.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_events.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_integration_small.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_perf_budget.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_error_paths.py
- tests/roguelike_game/ecs/systems/vendors/test_vendors_system_config.py

#### 1.3 Managers (buildings) (18)
- tests/roguelike_game/managers/buildings/test_loader_happy_path.py
- tests/roguelike_game/managers/buildings/test_loader_invalid_data.py
- tests/roguelike_game/managers/buildings/test_loader_io_errors.py
- tests/roguelike_game/managers/buildings/test_loader_perf_budget.py
- tests/roguelike_game/managers/buildings/test_loader_compat_schema.py
- tests/roguelike_game/managers/buildings/test_loader_regressions.py
- tests/roguelike_game/managers/buildings/test_updater_happy_path.py
- tests/roguelike_game/managers/buildings/test_updater_invalid_data.py
- tests/roguelike_game/managers/buildings/test_updater_io_errors.py
- tests/roguelike_game/managers/buildings/test_updater_perf_budget.py
- tests/roguelike_game/managers/buildings/test_updater_compat_schema.py
- tests/roguelike_game/managers/buildings/test_updater_regressions.py
- tests/roguelike_game/managers/buildings/test_calibrator_happy_path.py
- tests/roguelike_game/managers/buildings/test_calibrator_invalid_data.py
- tests/roguelike_game/managers/buildings/test_calibrator_io_errors.py
- tests/roguelike_game/managers/buildings/test_calibrator_perf_budget.py
- tests/roguelike_game/managers/buildings/test_calibrator_compat_schema.py
- tests/roguelike_game/managers/buildings/test_calibrator_regressions.py

#### 1.4 Utils (inventario) (8)
- tests/roguelike_game/utils/inventory_sync/test_inventory_sync_basic.py [COMPLETADO]
- tests/roguelike_game/utils/inventory_sync/test_inventory_sync_conflicts.py
- tests/roguelike_game/utils/inventory_sync/test_inventory_sync_perf.py
- tests/roguelike_game/utils/inventory_sync/test_inventory_sync_idempotency.py [COMPLETADO]
- tests/roguelike_game/utils/inventory_registry/test_inventory_registry_register.py [COMPLETADO]
- tests/roguelike_game/utils/inventory_registry/test_inventory_registry_lookup.py [COMPLETADO]
- tests/roguelike_game/utils/inventory_registry/test_inventory_registry_persistence.py
- tests/roguelike_game/utils/inventory_registry/test_inventory_registry_errors.py [COMPLETADO]

#### 1.5 Integración (20)
- tests/roguelike_game/integration/test_integration_gameplay_flow_01.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_02.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_03.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_04.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_05.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_06.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_07.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_08.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_09.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_10.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_11.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_12.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_13.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_14.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_15.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_16.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_17.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_18.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_19.py
- tests/roguelike_game/integration/test_integration_gameplay_flow_20.py

---

### 2) roguelike_engine (160)

#### 2.1 Mapa (controller, events, helpers, model, services, view) (72)
- tests/roguelike_engine/map/controller/test_controller_happy_path.py
- tests/roguelike_engine/map/controller/test_controller_edge_cases.py
- tests/roguelike_engine/map/controller/test_controller_serialization.py
- tests/roguelike_engine/map/controller/test_controller_error_paths.py
- tests/roguelike_engine/map/controller/test_controller_perf_budget.py
- tests/roguelike_engine/map/controller/test_controller_validation.py
- tests/roguelike_engine/map/controller/test_controller_integration_small.py
- tests/roguelike_engine/map/controller/test_controller_tiles_interop.py
- tests/roguelike_engine/map/controller/test_controller_compat_schema.py
- tests/roguelike_engine/map/controller/test_controller_resource_leaks.py
- tests/roguelike_engine/map/controller/test_controller_regressions.py
- tests/roguelike_engine/map/controller/test_controller_fuzz_inputs.py
- tests/roguelike_engine/map/events/test_events_happy_path.py
- tests/roguelike_engine/map/events/test_events_edge_cases.py
- tests/roguelike_engine/map/events/test_events_serialization.py
- tests/roguelike_engine/map/events/test_events_error_paths.py
- tests/roguelike_engine/map/events/test_events_perf_budget.py
- tests/roguelike_engine/map/events/test_events_validation.py
- tests/roguelike_engine/map/events/test_events_integration_small.py
- tests/roguelike_engine/map/events/test_events_tiles_interop.py
- tests/roguelike_engine/map/events/test_events_compat_schema.py
- tests/roguelike_engine/map/events/test_events_resource_leaks.py
- tests/roguelike_engine/map/events/test_events_regressions.py
- tests/roguelike_engine/map/events/test_events_fuzz_inputs.py
- tests/roguelike_engine/map/helpers/test_helpers_happy_path.py [COMPLETADO]
- tests/roguelike_engine/map/helpers/test_helpers_error_paths.py [COMPLETADO]
- tests/roguelike_engine/map/model/test_model_happy_path.py [COMPLETADO]
- tests/roguelike_engine/map/model/test_model_edge_cases.py
- tests/roguelike_engine/map/model/test_model_serialization.py
- tests/roguelike_engine/map/model/test_model_error_paths.py [COMPLETADO]
- tests/roguelike_engine/map/model/test_model_perf_budget.py
- tests/roguelike_engine/map/model/test_model_validation.py
- tests/roguelike_engine/map/model/test_model_integration_small.py
- tests/roguelike_engine/map/model/test_model_tiles_interop.py
- tests/roguelike_engine/map/model/test_model_compat_schema.py
- tests/roguelike_engine/map/model/test_model_resource_leaks.py
- tests/roguelike_engine/map/model/test_model_regressions.py
- tests/roguelike_engine/map/model/test_model_fuzz_inputs.py
- tests/roguelike_engine/map/services/test_services_happy_path.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_edge_cases.py
- tests/roguelike_engine/map/services/test_services_serialization.py
- tests/roguelike_engine/map/services/test_services_error_paths.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_perf_budget.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_validation.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_integration_small.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_tiles_interop.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_compat_schema.py [COMPLETADO]
- tests/roguelike_engine/map/services/test_services_resource_leaks.py
- tests/roguelike_engine/map/services/test_services_regressions.py
- tests/roguelike_engine/map/services/test_services_fuzz_inputs.py
- tests/roguelike_engine/map/view/test_view_happy_path.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_edge_cases.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_serialization.py
- tests/roguelike_engine/map/view/test_view_error_paths.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_perf_budget.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_validation.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_integration_small.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_tiles_interop.py [COMPLETADO]
- tests/roguelike_engine/map/view/test_view_compat_schema.py
- tests/roguelike_engine/map/view/test_view_resource_leaks.py
- tests/roguelike_engine/map/view/test_view_regressions.py
- tests/roguelike_engine/map/view/test_view_fuzz_inputs.py

#### 2.2 Consola (18)
- tests/roguelike_engine/console/command_sets/test_command_sets_process.py
- tests/roguelike_engine/console/command_sets/test_command_sets_parse.py
- tests/roguelike_engine/console/command_sets/test_command_sets_permissions.py
- tests/roguelike_engine/console/command_sets/test_command_sets_error_paths.py
- tests/roguelike_engine/console/command_sets/test_command_sets_help_text.py
- tests/roguelike_engine/console/command_sets/test_command_sets_integration_small.py
- tests/roguelike_engine/console/commands/test_commands_process.py
- tests/roguelike_engine/console/commands/test_commands_parse.py
- tests/roguelike_engine/console/commands/test_commands_permissions.py
- tests/roguelike_engine/console/commands/test_commands_error_paths.py
- tests/roguelike_engine/console/commands/test_commands_help_text.py
- tests/roguelike_engine/console/commands/test_commands_integration_small.py
- tests/roguelike_engine/console/contexts/test_contexts_process.py
- tests/roguelike_engine/console/contexts/test_contexts_parse.py
- tests/roguelike_engine/console/contexts/test_contexts_permissions.py
- tests/roguelike_engine/console/contexts/test_contexts_error_paths.py
- tests/roguelike_engine/console/contexts/test_contexts_help_text.py
- tests/roguelike_engine/console/contexts/test_contexts_integration_small.py

#### 2.3 Diagnóstico (16)
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_record_cycle.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_buffer_wraparound.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_file_rotation.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_error_paths.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_sampling_perf.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_json_schema.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_retention_policy.py
- tests/roguelike_engine/diagnostics/overlay/services/test_overlay_services_concurrent_writes.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_record_cycle.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_buffer_wraparound.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_file_rotation.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_error_paths.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_sampling_perf.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_json_schema.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_retention_policy.py
- tests/roguelike_engine/diagnostics/recorder_core/test_recorder_core_concurrent_writes.py

#### 2.4 Input (8)
- tests/roguelike_engine/input/test_input_mapping_basic.py
- tests/roguelike_engine/input/test_input_event_queue.py
- tests/roguelike_engine/input/test_input_debouncing.py
- tests/roguelike_engine/input/test_input_device_hotplug.py
- tests/roguelike_engine/input/test_input_repeat_rate.py
- tests/roguelike_engine/input/test_input_chords.py
- tests/roguelike_engine/input/test_input_error_paths.py
- tests/roguelike_engine/input/test_input_integration_small.py

#### 2.5 Tile y utils (8)
- tests/roguelike_engine/tile/test_tile_grid_indexing.py
- tests/roguelike_engine/tile/test_tile_atlas_loading.py
- tests/roguelike_engine/tile/test_tile_animation_steps.py
- tests/roguelike_engine/tile/test_tile_z_layer_ordering.py
- tests/roguelike_engine/tile/test_tile_error_paths.py
- tests/roguelike_engine/tile/utils/test_utils_neighbors.py
- tests/roguelike_engine/tile/utils/test_utils_pathfinding.py
- tests/roguelike_engine/tile/utils/test_utils_serialization.py

#### 2.6 World (8)
- tests/roguelike_engine/world/test_world_entity_lifecycle.py
- tests/roguelike_engine/world/test_world_query_archetypes.py
- tests/roguelike_engine/world/test_world_system_schedule.py
- tests/roguelike_engine/world/test_world_save_load.py
- tests/roguelike_engine/world/test_world_error_paths.py
- tests/roguelike_engine/world/test_world_perf_budget.py
- tests/roguelike_engine/world/test_world_integration_small.py
- tests/roguelike_engine/world/test_world_schema_compat.py

#### 2.7 Buildings (20)
- tests/roguelike_engine/buildings/model_mixins/test_model_mixins_happy_path.py
- tests/roguelike_engine/buildings/model_mixins/test_model_mixins_edge_cases.py
- tests/roguelike_engine/buildings/model_mixins/test_model_mixins_serialization.py
- tests/roguelike_engine/buildings/model_mixins/test_model_mixins_perf_budget.py
- tests/roguelike_engine/buildings/model_mixins/test_model_mixins_regressions.py
- tests/roguelike_engine/buildings/model_utils/test_model_utils_happy_path.py
- tests/roguelike_engine/buildings/model_utils/test_model_utils_edge_cases.py
- tests/roguelike_engine/buildings/model_utils/test_model_utils_serialization.py
- tests/roguelike_engine/buildings/model_utils/test_model_utils_perf_budget.py
- tests/roguelike_engine/buildings/model_utils/test_model_utils_regressions.py
- tests/roguelike_engine/buildings/rendering/test_rendering_happy_path.py
- tests/roguelike_engine/buildings/rendering/test_rendering_edge_cases.py
- tests/roguelike_engine/buildings/rendering/test_rendering_serialization.py
- tests/roguelike_engine/buildings/rendering/test_rendering_perf_budget.py
- tests/roguelike_engine/buildings/rendering/test_rendering_regressions.py
- tests/roguelike_engine/buildings/services/test_services_happy_path.py
- tests/roguelike_engine/buildings/services/test_services_edge_cases.py
- tests/roguelike_engine/buildings/services/test_services_serialization.py
- tests/roguelike_engine/buildings/services/test_services_perf_budget.py
- tests/roguelike_engine/buildings/services/test_services_regressions.py

#### 2.8 Chat (4)
- tests/roguelike_engine/chat/providers/test_providers_happy_path.py
- tests/roguelike_engine/chat/providers/test_providers_error_paths.py
- tests/roguelike_engine/chat/providers/test_providers_schema_compat.py
- tests/roguelike_engine/chat/service/test_service_happy_path.py

#### 2.9 Utils (2)
- tests/roguelike_engine/utils/test_utils_time_budget.py
- tests/roguelike_engine/utils/test_utils_math_safe_ops.py

#### 2.10 Cámara (2)
- tests/roguelike_engine/camera/test_camera_follow_target.py
- tests/roguelike_engine/camera/test_camera_viewport_clamp.py

#### 2.11 Minimap (2)
- tests/roguelike_engine/minimap/test_minimap_rasterize.py
- tests/roguelike_engine/minimap/test_minimap_perf_budget.py

---

### 3) roguelike_ui (50)

#### 3.1 Text input (10)
- tests/roguelike_ui/widgets/text_input/test_text_input_caret_movement.py
- tests/roguelike_ui/widgets/text_input/test_text_input_selection.py [COMPLETADO]
- tests/roguelike_ui/widgets/text_input/test_text_input_wrapping.py [COMPLETADO]
- tests/roguelike_ui/widgets/text_input/test_text_input_backspace_delete.py [COMPLETADO]
- tests/roguelike_ui/widgets/text_input/test_text_input_clipboard.py
- tests/roguelike_ui/widgets/text_input/test_text_input_paste_sanitization.py
- tests/roguelike_ui/widgets/text_input/test_text_input_ime_input.py
- tests/roguelike_ui/widgets/text_input/test_text_input_shortcuts.py
- tests/roguelike_ui/widgets/text_input/test_text_input_rendering_single.py [COMPLETADO]
- tests/roguelike_ui/widgets/text_input/test_text_input_rendering_wrapped.py [COMPLETADO]

#### 3.2 Widgets núcleo (18)
- tests/roguelike_ui/widgets/button/test_button_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/button/test_button_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/grid/test_grid_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/grid/test_grid_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/hover/test_hover_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/hover/test_hover_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/file_system_picker/test_file_system_picker_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/file_system_picker/test_file_system_picker_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/double_click_detector/test_double_click_detector_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/double_click_detector/test_double_click_detector_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/icon_cache/test_icon_cache_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/icon_cache/test_icon_cache_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/list_panel_ui/test_list_panel_ui_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/list_panel_ui/test_list_panel_ui_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/tab_panel_ui/test_tab_panel_ui_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/tab_panel_ui/test_tab_panel_ui_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/map_items_ui/test_map_items_ui_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/map_items_ui/test_map_items_ui_edge_cases.py [COMPLETADO]

#### 3.3 Servicios (6)
- tests/roguelike_ui/services/test_formatting_happy_path.py [COMPLETADO]
- tests/roguelike_ui/services/test_formatting_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/services/test_formatting_internationalization.py [COMPLETADO]
- tests/roguelike_ui/services/test_json_persistence_happy_path.py [COMPLETADO]
- tests/roguelike_ui/services/test_json_persistence_error_paths.py [COMPLETADO]
- tests/roguelike_ui/services/test_json_persistence_schema_compat.py

#### 3.4 Panels y helpers (12)
- tests/roguelike_ui/test_panel_happy_path.py [COMPLETADO]
- tests/roguelike_ui/test_panel_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/toolbar_panel/test_toolbar_panel_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/toolbar_panel/test_toolbar_panel_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/title_panel/test_title_panel_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/title_panel/test_title_panel_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/widgets/title_bar/test_title_bar_happy_path.py [COMPLETADO]
- tests/roguelike_ui/widgets/title_bar/test_title_bar_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/test_ui_blocker_happy_path.py [COMPLETADO]
- tests/roguelike_ui/test_ui_blocker_edge_cases.py [COMPLETADO]
- tests/roguelike_ui/test_ui_helpers_happy_path.py [COMPLETADO]
- tests/roguelike_ui/test_ui_helpers_edge_cases.py

#### 3.5 Menús y opciones (4)
- tests/roguelike_ui/widgets/options_configurator/test_options_configurator_happy_path.py
- tests/roguelike_ui/widgets/options_configurator/test_options_configurator_edge_cases.py
- tests/roguelike_ui/widgets/menu_configurator/test_menu_configurator_happy_path.py
- tests/roguelike_ui/widgets/menu_renderer/test_menu_renderer_happy_path.py

---

### 4) roguelike_editors (30)

#### 4.1 Spawner (servicios de persistencia) (6)
- tests/roguelike_editors/spawner/services/test_persistence_save_load.py [COMPLETADO]
- tests/roguelike_editors/spawner/services/test_persistence_validation.py [COMPLETADO]
- tests/roguelike_editors/spawner/services/test_persistence_schema_compat.py
- tests/roguelike_editors/spawner/services/test_persistence_io_errors.py
- tests/roguelike_editors/spawner/services/test_persistence_perf_budget.py
- tests/roguelike_editors/spawner/services/test_persistence_regressions.py

#### 4.2 Tiles (tiles_view_panel) (8)
- tests/roguelike_editors/tiles/tiles_view_panel/test_controller_happy_path.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_controller_events.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_view_render.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_state_transitions.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_selection.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_pagination.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_perf_budget.py
- tests/roguelike_editors/tiles/tiles_view_panel/test_regressions.py

#### 4.3 Map (view) (6)
- tests/roguelike_editors/map/view/test_dialogs.py
- tests/roguelike_editors/map/view/test_colors.py
- tests/roguelike_editors/map/view/test_colliders_view.py
- tests/roguelike_editors/map/view/test_zones_view.py
- tests/roguelike_editors/map/view/test_fonts.py
- tests/roguelike_editors/map/view/test_progress.py

#### 4.4 Common UI (4)
- tests/roguelike_editors/common/ui/test_layout.py
- tests/roguelike_editors/common/ui/test_input_events.py
- tests/roguelike_editors/common/ui/test_validation.py
- tests/roguelike_editors/common/ui/test_regressions.py

#### 4.5 Entities (controller/events) (6)
- tests/roguelike_editors/entities/controller/events/test_event_routing.py
- tests/roguelike_editors/entities/controller/events/test_undo_redo.py
- tests/roguelike_editors/entities/controller/events/test_selection.py
- tests/roguelike_editors/entities/controller/events/test_drag_drop.py
- tests/roguelike_editors/entities/controller/events/test_keyboard_shortcuts.py
- tests/roguelike_editors/entities/controller/events/test_serialization.py

---

## Notas de uso
- Esta lista prioriza coherencia y trazabilidad con el árbol de `src/`. Puedes crear carpetas según sea necesario.
- Recomendado: acompañar cada test con fixtures mínimas en `tests/fixtures/` cuando aplique.
- Orden sugerido: comenzar por `roguelike_game/ecs` y `roguelike_engine/map`, luego UI, y por último editores.
