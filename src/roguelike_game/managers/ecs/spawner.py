"""
Spawner de ECS: maneja spawn de jugador y NPCs.
"""
from roguelike_game.factories.player.player_factory import spawn_player_tile
from roguelike_engine.config.map_config import global_map_settings

class ECSSpawner:
    """
    Spawnea jugador y NPCs iniciales.
    """
    def spawn_player(self, ecs_world, map_manager):
        tx, ty = self._get_initial_player_tile(ecs_world, map_manager)
        pid = spawn_player_tile(ecs_world, tx, ty)
        ecs_world.player_entity = pid
        map_manager.spawn_player((tx, ty))

    def spawn_initial_npcs(self, ecs_world):
        ecs_world.spawn_npc_manager.spawn_npc_initial()

    def _get_initial_player_tile(self, ecs_world, map_manager):
        saved = map_manager._local_state.get("player_pos")
        if isinstance(saved, (tuple, list)) and len(saved) == 2:
            return saved
        off_x, off_y = map_manager.lobby_offset
        from roguelike_engine.config.map_config import global_map_settings
        return (
            off_x + global_map_settings.zone_width // 2,
            off_y + global_map_settings.zone_height // 2,
        )
