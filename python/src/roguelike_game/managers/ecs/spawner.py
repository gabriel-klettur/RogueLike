"""
Spawner de ECS: maneja el spawn de jugador.
"""
from roguelike_game.factories.registry import get_factory
from roguelike_engine.config.map_config import global_map_settings


class ECSSpawner:
    """
    Spawnea jugador.
    """
    def spawn_player(self, ecs_world, map_manager):
        tx, ty = self._get_initial_player_tile(ecs_world, map_manager)
        pid = get_factory("player").create(ecs_world, tile_x=tx, tile_y=ty)
        ecs_world.player_entity = pid
        map_manager.spawn_player((tx, ty))

    def _get_initial_player_tile(self, ecs_world, map_manager):
        saved = map_manager._local_state.get("player_pos")
        if isinstance(saved, (tuple, list)) and len(saved) == 2:
            return saved
        off_x, off_y = map_manager.lobby_offset
        return (
            off_x + global_map_settings.zone_width // 2,
            off_y + global_map_settings.zone_height // 2,
        )
