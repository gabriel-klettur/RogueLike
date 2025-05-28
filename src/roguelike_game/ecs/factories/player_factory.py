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
from roguelike_game.config_player import ORIGINAL_SPRITE_SIZE, PLAYER_SPEED, RENDERED_SPRITE_SIZE, PLAYER_STATS
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.systems.config_z_layer import Z_LAYERS
from roguelike_game.ecs.assets.player_assets import PlayerAssets
import time
import pygame


def spawn_player(world, x, y, character_name: str = "first_hero") -> int:
    """
    Crea la entidad jugador y añade los componentes básicos.

    Args:
        world: instancia de NPCWorld
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
    body_mask = pygame.mask.from_surface(orig_image)
    body = MaskCollider(body_mask, 0, 0)
    # Dimensiones y offsets para centrar feet collider
    w, h = RENDERED_SPRITE_SIZE
    fw = w // 2
    fh = h // 4
    feet_offset_x = (w - fw) // 2
    feet_offset_y = h - (fh // 2)
    feet = Collider(fw, fh, feet_offset_x, feet_offset_y)
    world.components["MultiCollider"][eid] = MultiCollider({"body": body, "feet": feet})
    # Componente de salud
    max_hp = PLAYER_STATS.get(character_name, {}).get("max_health", 100)
    world.components["Health"][eid] = Health(max_hp, max_hp)
    # Componente de combate
    world.components["CombatStats"][eid] = CombatStats(max_hp, max_hp, 1, 0)
    # Componente de arma cuerpo a cuerpo
    world.components["MeleeWeapon"][eid] = MeleeWeapon(damage=1, cooldown=1.0)
    return eid
