"""
Module: player_factory.py
Builder para crear la entidad jugador con todos sus componentes ECS.
"""
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.camera_follow import CameraFollowComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.systems.config_z_layer import Z_LAYERS
from roguelike_game.ecs.assets.player_assets import PlayerAssets
from roguelike_game.ecs.components.rendering.trail_component import TrailComponent, TrailConfig
import time
import pygame
import json
from pathlib import Path

# Load player config from JSON
_config_path = Path(__file__).resolve().parents[4] / "data" / "players.json"
with open(_config_path) as _f:
    _player_cfg = json.load(_f)
ORIGINAL_SPRITE_SIZE = tuple(_player_cfg["ORIGINAL_SPRITE_SIZE"])
PLAYER_SPEED = _player_cfg["PLAYER_SPEED"]
PLAYER_STATS = _player_cfg["PLAYER_STATS"]


def spawn_player(world, x, y, character_name: str = "first_hero") -> int:
    """
    Crea la entidad jugador y añade los componentes básicos.

    Args:
        world: instancia de ECSWorld
        x, y: coordenadas iniciales en píxeles
        character_name: identificador de sprite/asset del jugador
    Returns:
        eid: ID de la entidad creada
    """
    eid = world.create_entity()
    # Componente de posición
    world.components["Position"][eid] = Position(x, y)
    # Etiqueta de jugador
    world.components["PlayerTagComponent"][eid] = PlayerTagComponent()
    # Componente de cámara
    world.components["CameraFollowComponent"][eid] = CameraFollowComponent()
    # Componente de entrada
    world.components["InputComponent"][eid] = InputComponent()
    # Componente ZLayer para renderizado de jugador
    world.components["ZLayer"][eid] = ZLayer(Z_LAYERS["player"])
    # Cargar sprites del jugador
    sprites_dict, _ = PlayerAssets(character_name, ORIGINAL_SPRITE_SIZE).get_sprites()
    # Sprite inicial: primer frame idle 'down'
    down_idle = sprites_dict.get('down', {}).get('idle', [])
    if down_idle:
        sprite = Sprite(down_idle[0])
        world.components["Sprite"][eid] = sprite
    # Animator: mapear animaciones idle y walk separadas por dirección
    anim_map = {}
    for direction, frames in sprites_dict.items():
        anim_map[f"{direction}_idle"] = frames.get('idle', [])
        anim_map[f"{direction}_walk"] = frames.get('walk', [])
    world.components["Animator"][eid] = Animator(animations=anim_map, current_state='down_idle')
    # Control de velocidad de animación (pies caminando)
    world.components["AnimationTimer"][eid] = AnimationTimer(last_time=time.time(), interval=0.15)
    # Componente de movimiento
    world.components["MovementSpeed"][eid] = MovementSpeed(PLAYER_SPEED)
    # Componente de velocidad
    world.components["Velocity"][eid] = Velocity(0, 0)
    # Componente de colisión múltiple (body con MaskCollider y feet)
    # Crear body basado en la máscara del sprite (primer frame 'down')
    orig_image = sprite.image
    body = MaskCollider(pygame.mask.from_surface(orig_image), 0, 0)
    # Dimensiones reales del sprite para feet collider
    w_img, h_img = orig_image.get_size()
    fw = w_img // 2
    fh = h_img // 4
    feet_offset_x = (w_img - fw) // 2                 # centrar horizontalmente
    feet_offset_y = h_img - fh                        # alinear parte superior del collider con la base del sprite
    feet = Collider(fw, fh, feet_offset_x, feet_offset_y)
    world.components["MultiCollider"][eid] = MultiCollider({"body": body, "feet": feet})
    # Componente de salud
    max_hp = PLAYER_STATS.get(character_name, {}).get("max_health", 100)
    world.components["Health"][eid] = Health(max_hp, max_hp)
    # Componente de combate
    world.components["CombatStats"][eid] = CombatStats(max_hp, max_hp, 1, 0)
    # Componente de arma cuerpo a cuerpo
    world.components["MeleeWeapon"][eid] = MeleeWeapon(damage=1, cooldown=1.0)
    # Trail de sombra
    trail_cfg = TrailConfig(interval=0.1, life_time=0.5, max_trails=10)
    world.components["TrailComponent"][eid] = TrailComponent(config=trail_cfg)
    return eid


def spawn_player_tile(world, tile_x: int, tile_y: int, character_name: str = "first_hero") -> int:
    """
    Crea la entidad jugador usando coordenadas de tile.
    Calcula la posición en píxeles para alinear el collider 'feet' al centro del tile.
    """
    from roguelike_game.ecs.assets.player_assets import PlayerAssets
    from roguelike_engine.config.config_tiles import TILE_SIZE

    # Obtener sprite para medir dimensiones
    sprites_dict, _ = PlayerAssets(character_name, ORIGINAL_SPRITE_SIZE).get_sprites()
    down_idle = sprites_dict.get('down', {}).get('idle', [])
    if not down_idle:
        # Fallback: uso esquina superior izquierda del tile
        px = tile_x * TILE_SIZE
        py = tile_y * TILE_SIZE
    else:
        img = down_idle[0]
        w_img, h_img = img.get_size()
        fh = h_img // 4
        half_fh = fh // 2
        # Centro del tile en píxeles
        cx = tile_x * TILE_SIZE + TILE_SIZE // 2
        cy = tile_y * TILE_SIZE + TILE_SIZE // 2
        # Calcular top-left del sprite para alinear feet
        px = cx - w_img // 2
        py = cy - (h_img - half_fh)
    return spawn_player(world, px, py, character_name)
