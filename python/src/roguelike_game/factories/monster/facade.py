"""
Fachada de la fábrica de monstruos: registro y orquestación.
"""
from roguelike_game.factories.base import Factory
from roguelike_game.factories.registry import register_factory
from roguelike_game.factories.monster.builder import MonsterBuilder
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_game.factories.monster.calibrator import calibrate_tile_position

# Valor por defecto: primer tipo de monstruo definido en el config
DEFAULT_MONSTER = next(iter(MONSTER_DEFS))

@register_factory("monster")
class MonsterFactory(Factory):
    """Fábrica de monstruos, soporta coordenadas en píxeles o tiles."""

    def create(self, world, *, x: int | None = None, y: int | None = None,
               tile_x: int | None = None, tile_y: int | None = None,
               monster_type: str = DEFAULT_MONSTER,
               instance_id: str | None = None) -> int:
        # Calibrar si usan coords de tile
        if tile_x is not None and tile_y is not None:
            x, y = calibrate_tile_position(tile_x, tile_y, monster_type)
        if x is None or y is None:
            raise ValueError("Debe proveer x,y o tile_x,tile_y al crear el monstruo.")
        return MonsterBuilder(world).build(x, y, monster_type, instance_id=instance_id)
