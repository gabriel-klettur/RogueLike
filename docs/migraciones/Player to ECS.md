Plan de alto nivel para migrar tu clase Player al ECS:

Auditoría inicial
Revisa entities/player.py y apunta todos sus atributos (posición, sprite, stats, inventario, lógica de entrada, cámara, etc.) y métodos (move, attack, update, render…).

- PlayerData (`src/roguelike_game/entities/player/model/player_data.py`):  
  - last_position: Dict[str, tuple[int, int]]  
  - Métodos: to_dict(), from_dict()  
- PlayerController (`src/roguelike_game/entities/player/controller/player_controller.py`):  
  - Atributos: state, model, stats, movement, attack, obstacles, player_view, hud_view, renderer  
  - Métodos: __init__(), render(), update(), render_hud(), handle_input(), change_character(), center(), restore_all(), move(), take_damage()  
- PlayerModel (`src/roguelike_game/entities/player/model/player_model.py`):  
  - Atributos: x, y, class_player, sprite_sheet_size, sprite_size, direction, is_walking, rect, hitbox_obj, stats, movement, attack  
  - Métodos: center(), hitbox()  
- PlayerStats (`src/roguelike_game/entities/player/model/stats_model.py`):  
  - Atributos: max_health, max_mana, max_energy, health, mana, energy, shield_points, last_restore_time, last_shield_time, last_firework_time, last_smoke_time, last_lightning_time, last_pixel_fire_time  
  - Métodos: restore_all(), activate_shield()  
- PlayerMovement (`src/roguelike_game/entities/player/model/movement_model.py`):  
  - Atributos: speed, is_dashing, dash_speed, dash_duration, last_dash_time, dash_cooldown, dash_time_left, dash_direction, teleport_cooldown, last_teleport_time, teleport_distance  
  - Métodos: move(), update_dash(), teleport(), hitbox()  
- PlayerAttack (`src/roguelike_game/entities/player/model/player_attack.py`):  
  - Métodos: perform_basic_attack(), perform_skill(), etc.  
- PlayerView (`src/roguelike_game/entities/player/view/player_view.py`):  
  - Atributos: state, sprites, _cached_fonts, _scaled_icons  
  - Métodos: get_font(), render()  

Diseñar componentes ECS
Para cada dato del jugador, crea o reutiliza los siguientes componentes ECS (en `src/roguelike_game/ecs/components`):
- `Position` (`transform/position.py`)
- `Sprite` (+ opcional `Animator`) (`render/sprite.py`, `render/animator.py`)
- `CombatStats` (`combat/combat_stats.py`)
- `Health` (`combat/health.py`)
- `Velocity` (`physics/velocity.py`)
- `MovementSpeed` (`physics/movement_speed.py`)
- `Collider` / `MultiCollider` (`physics/collider.py`)
- `AttackCooldown` (`combat/attack_cooldown.py`)
- `WantsToMelee` (`combat/wants_to_melee.py`)
- `DeathTimer` (`combat/death_timer.py`)
- `InventoryComponent` (nuevo, `combat/inventory.py`)
- `InputComponent` (nuevo, `input/input_component.py`)
- `CameraFollowComponent` (nuevo, `core/camera_follow.py`)
- `PlayerTagComponent` (nuevo, `core/player_tag.py`)

## Implementación
- Creado `ecs/components/combat/inventory.py` con `InventoryComponent`
- Creado `ecs/components/input_component.py` con `InputComponent`
- Creado `ecs/components/core/camera_follow.py` con `CameraFollowComponent`
- Creado `ecs/components/core/player_tag.py` con `PlayerTagComponent`
- Registrado en `NPCWorld._init_components` las claves `InputComponent`, `InventoryComponent`, `CameraFollowComponent`, `PlayerTagComponent`
- Creado `ecs/factories/player_factory.py` con `spawn_player` para generar al jugador con Position, PlayerTag, CameraFollow e Input
- Integrado `spawn_player` en `ECSManager.__init__` para crear la entidad jugador y almacenar su ID en `ecs_world.player_entity`

Crear un PlayerFactory o builder
Un módulo que en la carga de escena haga:
eid = world.create_entity()
world.add_component(eid, PositionComponent(...))
…
world.add_component(eid, PlayerTagComponent())

Migración progresiva por áreas de responsabilidad

Fase 1: Render & posición
- Extrae la lógica de render de `PlayerView` a `RenderSystem` (`ecs/systems/rendering/render_system.py`) usando `Position` + `Sprite` (+ `Animator`).

Fase 2: Entrada & movimiento
+ - `spawn_player` añade los componentes `InputComponent`, `Velocity`, `MovementSpeed` y `MultiCollider`.
+ - Implementa `InputSystem` (`ecs/systems/input/input_system.py`) que traduzca eventos de Pygame a `InputComponent` y actualice `Velocity`.
+ - Registra `InputSystem` en `NPCWorld._init_systems`.
+ - Reutiliza `MovementCollisionSystem` y `FacingSystem` (`ecs/systems/physics`) para mover y orientar al jugador.
+ - Utiliza `spawn_player` para crear la entidad jugador.

Fase 3: Combate & cooldown
- En `ecs/systems/input/input_system.py` (`InputSystem`), al pulsar `SPACE`, genera `WantsToMelee` y añade `AttackCooldown`.
- Reutiliza `MeleeCombatSystem` (`ecs/systems/combat/melee_combat_system.py`) para procesar `WantsToMelee` y aplicar daño.
- Utiliza `DeathTimerSystem`, `DeathTimerBarSystem` o `DeathTimerDebugSystem` para manejar la muerte cuando `Health.current_hp` ≤ 0.

Fase 4: Inventario & UI
- Refactoriza la UI de inventario para leer de `InventoryComponent`.

Ajuste de sistemas genéricos
Revisa tus sistemas (patrol, ai, camera, etc.) y añade filtros por PlayerTagComponent cuando necesites lógica específica al jugador.

Pruebas y validación
Tras cada fase, testea movimiento, combate y demás.
Añade tests unitarios para cada sistema y componente del jugador.

Depuración y limpieza
Una vez probada la migración completa, elimina entities/player.py y cualquier resto de lógica “vieja”.
Verifica que no queden referencias colgando y actualiza la documentación/README.