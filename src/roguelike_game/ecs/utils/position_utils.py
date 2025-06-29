# Path: src/roguelike_game/ecs/utils/position_utils.py
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from pygame.math import Vector2


def compute_entity_center(pos: Position, sprite: Sprite, scale_cmp: Scale, zoom: float = 1.0) -> Vector2:
    """
    Devuelve la posición mundial del centro del sprite considerando escala.
    """
    s = scale_cmp.scale if scale_cmp else 1.0
    w, h = sprite.image.get_size()
    cx = pos.x + (w * s) / 2
    cy = pos.y + (h * s) / 2
    return Vector2(cx, cy)


def compute_foot_world_position(pos: Position, sprite: Sprite, scale_cmp: Scale) -> Vector2:
    """
    Devuelve la posición mundial de los pies (punto inferior medio) considerando escala.
    """
    s = scale_cmp.scale if scale_cmp else 1.0
    w, h = sprite.image.get_size()
    fx = pos.x + (w * s) / 2
    fy = pos.y + (h * s)
    return Vector2(fx, fy)


def compute_foot_tile(world, eid: int, tile_size: int) -> tuple[int,int] | None:
    """
    Dado world y entity_id, calcula la coordenada de tile en la que quedan sus pies.
    """
    comps = world.components
    pos = comps.get('Position', {}).get(eid)
    sprite = comps.get('Sprite', {}).get(eid)
    scale_cmp = comps.get('Scale', {}).get(eid)
    if not pos or not sprite:
        return None
    foot = compute_foot_world_position(pos, sprite, scale_cmp)
    return (int(foot.x) // tile_size, int(foot.y) // tile_size)