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
from roguelike_game.entities.player.config_player import ORIGINAL_SPRITE_SIZE, PLAYER_SPEED, RENDERED_SPRITE_SIZE
from roguelike_game.entities.player.view.assets import PlayerAssets
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator


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
    # Componente de renderizado (Sprite y Animator) - solo estado idle
    sprites_dict, _ = PlayerAssets(character_name, ORIGINAL_SPRITE_SIZE).get_sprites()
    idle_frames = sprites_dict.get('down', {}).get('idle', [])
    if idle_frames:
        world.components["Sprite"][eid] = Sprite(idle_frames[0])
    # Animator con frames idle
    world.components["Animator"][eid] = Animator(animations={'idle': idle_frames}, current_state='idle')
    # Componente de movimiento
    world.components["MovementSpeed"][eid] = MovementSpeed(PLAYER_SPEED)
    # Componente de velocidad
    world.components["Velocity"][eid] = Velocity(0, 0)
    # Componente de colisión múltiple (body y feet)
    w, h = RENDERED_SPRITE_SIZE
    body = Collider(w, h, 0, 0)
    feet = Collider(w//2, h//4, w//4, 3*h//4)
    world.components["MultiCollider"][eid] = MultiCollider({"body": body, "feet": feet})
    return eid
