"""
Fachada de la fábrica de jugador: registro y orquestación.
"""
from roguelike_game.factories.base import Factory
from roguelike_game.factories.registry import register_factory
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.factories.player.loader import load_and_scale_sprites, extract_initial_frame
from roguelike_game.factories.player.calibrator import calibrate_tile_position
from roguelike_game.factories.player.builder import PlayerBuilder
from roguelike_game.factories.player.config import DEFAULT_CLASS


@register_factory("player")
class PlayerFactory(Factory):
    """Fábrica de jugadores, soporta coordenadas en píxeles o tiles."""

    def create(self, world, *, x: int | None = None, y: int | None = None,
               tile_x: int | None = None, tile_y: int | None = None,
               class_player: str = DEFAULT_CLASS) -> int:
        # Compatibilidad para tests: usar ManagerClass.spawn_player_tile si se ha sobrecargado
        from roguelike_game.ecs.core.manager import ECSWorld as ManagerClass
        override_cls = getattr(ManagerClass, 'ECSWorld', None)
        spawn_method = getattr(ManagerClass, 'spawn_player_tile', None)
        if override_cls and callable(spawn_method) and tile_x is not None and tile_y is not None:
            return spawn_method(world, tile_x, tile_y)

        # Calibrar si usan coords de tile
        if tile_x is not None and tile_y is not None:
            sprites = load_and_scale_sprites(class_player)
            frame = extract_initial_frame(sprites)
            x, y = calibrate_tile_position(tile_x, tile_y, frame)

        if x is None or y is None:
            raise ValueError("Debe proveer x,y o tile_x,tile_y al crear el jugador.")

        return PlayerBuilder(world).build(x, y, class_player)
