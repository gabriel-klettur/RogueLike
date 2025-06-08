"""
Module: player_factory.py
Builder para crear la entidad jugador con todos sus componentes ECS.
"""

import time
import pygame

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.camera_follow import CameraFollowComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.rendering.trail_component import TrailComponent, TrailConfig
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.systems.config_z_layer import Z_LAYERS
from roguelike_game.ecs.assets.player_assets import PlayerAssets
from roguelike_game.ecs.factories.player.config import ORIGINAL_SPRITE_SIZE, PLAYER_STATS, DEFAULT_CLASS, DEFAULT_SCALE, DEFAULT_SPEED, ANIMATION_INTERVAL, INITIAL_ANIMATION_STATE, MELEE_WEAPON_CFG, DEFAULT_TRAIL, FEET_WIDTH_DIVISOR, FEET_HEIGHT_DIVISOR
from roguelike_game.ecs.factories.player.sprite_loader import load_and_scale_sprites, extract_initial_frame, build_animator_map
from roguelike_game.ecs.factories.player.collider import create_body_and_feet
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine


# --------------------------------------------
# Funciones principales: spawn_player y spawn_player_tile
# --------------------------------------------

def spawn_player(world, x: int, y: int, class_player: str = DEFAULT_CLASS) -> int:
    """
    Crea la entidad jugador y añade todos los componentes ECS necesarios.

    Args:
        world: instancia de ECSWorld.
        x, y: coordenadas iniciales en píxeles donde se ubica al jugador.
        class_player: Identificador de la clase/asset del jugador (por defecto DEFAULT_CLASS).

    Retorna:
        eid (int): ID de la entidad recién creada.
    """
    # 1) Crear entidad vacía
    eid = world.create_entity()

    # 2) Componente Position
    world.components["Position"][eid] = Position(x, y)

    # 3) Etiqueta de jugador (PlayerTag) y cámara (CameraFollow)
    world.components["PlayerTagComponent"][eid] = PlayerTagComponent()
    world.components["CameraFollowComponent"][eid] = CameraFollowComponent()

    # 4) Componente de entrada para el jugador
    world.components["InputComponent"][eid] = InputComponent()

    # 5) Capa Z para renderizado (asegura orden correcto en pantalla)
    world.components["ZLayer"][eid] = ZLayer(Z_LAYERS["player"])

    # ------------------------------------------------
    # 6) Sprites y animaciones
    # ------------------------------------------------

    sprites_dict = load_and_scale_sprites(class_player)

    initial_frame = extract_initial_frame(sprites_dict)
    if initial_frame:
        world.components["Sprite"][eid] = Sprite(initial_frame)

    anim_map = build_animator_map(sprites_dict)
    world.components["Animator"][eid] = Animator(
        animations=anim_map,
        current_state=INITIAL_ANIMATION_STATE,
    )

    world.components["AnimationTimer"][eid] = AnimationTimer(
        last_time=time.time(),
        interval=ANIMATION_INTERVAL,
    )

    # ------------------------------------------------
    # 7) Movimiento: velocidad y vector de velocidad
    # ------------------------------------------------

    speed_value = PLAYER_STATS[class_player]["speed"]
    world.components["MovementSpeed"][eid] = MovementSpeed(speed_value)

    world.components["Velocity"][eid] = Velocity(0, 0)

    # ------------------------------------------------
    # 8) Colisiones: cuerpo (mask) y pies (rect)
    # ------------------------------------------------

    if initial_frame:
        multi_collider = create_body_and_feet(initial_frame)
        world.components["MultiCollider"][eid] = multi_collider

    # ------------------------------------------------
    # 9) Salud y estadísticas de combate
    # ------------------------------------------------

    max_hp = PLAYER_STATS[class_player]["max_health"]
    world.components["Health"][eid] = Health(max_hp, max_hp)

    # Si attack y defense están definidos en PLAYER_STATS, úsalos; 
    # de lo contrario, asumimos valores mínimos (1 y 0).
    attack_value = PLAYER_STATS[class_player]["attack"]
    defense_value = PLAYER_STATS[class_player]["defense"]
    world.components["CombatStats"][eid] = CombatStats(
        current_hp=max_hp,
        max_hp=max_hp,
        power=attack_value,
        defense=defense_value,
    )

    # ------------------------------------------------
    # 10) Arma cuerpo a cuerpo
    # ------------------------------------------------

    world.components["MeleeWeapon"][eid] = MeleeWeapon(
        damage=MELEE_WEAPON_CFG["damage"],
        cooldown=MELEE_WEAPON_CFG["cooldown"],
    )

    # ------------------------------------------------
    # 11) Efecto visual: Trail de sombra
    # ------------------------------------------------

    # Tomamos directamente del JSON: primero buscamos en la clase,
    # si no estuviera, heredamos de DEFAULT_TRAIL (forzando que esté definido allí).
    trail_params = PLAYER_STATS[class_player].get("trail", DEFAULT_TRAIL)
    trail_cfg = TrailConfig(
        interval=trail_params["interval"],
        life_time=trail_params["life_time"],
        max_trails=trail_params["max_trails"],
    )
    world.components["TrailComponent"][eid] = TrailComponent(config=trail_cfg)

    # ------------------------------------------------
    # 12) FSM del Player
    # ------------------------------------------------
    fsm = FiniteStateMachine(IdleState())
    world.components["NPCState"][eid] = NPCState(fsm, "IdleState")

    return eid


def spawn_player_tile(world, tile_x: int, tile_y: int, class_player: str = DEFAULT_CLASS) -> int:
    """
    Crea la entidad jugador usando coordenadas de tile en lugar de píxeles.
    Calcula la posición de píxeles para alinear el collider 'feet' al centro del tile.

    Args:
        world: instancia de ECSWorld.
        tile_x, tile_y: coordenadas en tiles (int).
        class_player: clase/asset del jugador.

    Retorna:
        eid (int): ID de la entidad creada.
    """
    # 1) Cargar sprites sin escalar (solo necesitamos el primer frame 'down_idle' para medir)
    sprites_dict, _ = PlayerAssets(class_player, ORIGINAL_SPRITE_SIZE).get_sprites()
    down_idle_frames = sprites_dict["down"]["idle"]

    if not down_idle_frames:
        # Si no existen sprites, usar esquina superior del tile
        px = tile_x * TILE_SIZE
        py = tile_y * TILE_SIZE
    else:
        first_frame = down_idle_frames[0]
        w_img, h_img = first_frame.get_size()

        feet_height = h_img // FEET_HEIGHT_DIVISOR
        half_feet = feet_height // 2

        cx = tile_x * TILE_SIZE + TILE_SIZE // 2
        cy = tile_y * TILE_SIZE + TILE_SIZE // 2

        px = cx - (w_img // 2)
        py = cy - (h_img - half_feet)

    return spawn_player(world, px, py, class_player)
