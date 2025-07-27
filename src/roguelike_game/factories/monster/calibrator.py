"""
Calibración de posición de monstruos basada en coordenadas de tile.
"""
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_game.factories.monster.sprite_loader import create_sprite_component
import roguelike_game.factories.monster.physics as physics


def calibrate_tile_position(tile_x: int, tile_y: int, monster_type: str) -> tuple[int, int]:
    """
    Transforma coordenadas de tile a píxeles usando configuración de monstruo.
    """
    cfg = MONSTER_DEFS[monster_type]
    sprite, _ = create_sprite_component(monster_type)
    return physics.calculate_position(tile_x, tile_y, cfg, sprite)
