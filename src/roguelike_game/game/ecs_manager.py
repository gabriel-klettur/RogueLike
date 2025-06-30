
# Path: src/roguelike_game/game/ecs_manager.py
from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.factories.player.player_factory import spawn_player_tile
from roguelike_engine.config.map_config import global_map_settings


class ECSManager:
    def __init__(self, screen, map_manager, entities_manager, perf_log):
        self.perf_log = perf_log
        self.screen = screen
        self.map_manager = map_manager
        self.entities_manager = entities_manager

        self.ecs_world = ECSWorld(
            screen,
            map_manager,
            entities_manager.buildings,
            perf_log
        )

        self._spawn_player()
        self._spawn_initial_npcs()

        self.entities_manager.ecs_manager = self
        self.ecs_world.entities_manager = self.entities_manager

    def _spawn_player(self):
        tx, ty = self._get_initial_player_tile()
        pid = spawn_player_tile(self.ecs_world, tx, ty)
        self.ecs_world.player_entity = pid
        self.map_manager.spawn_player((tx, ty))

    def _spawn_initial_npcs(self):
        self.ecs_world.spawn_npc_manager.spawn_npc_initial()

    def _get_initial_player_tile(self):
        saved = self.map_manager._local_state.get("player_pos")
        if isinstance(saved, (tuple, list)) and len(saved) == 2:
            return saved
        off_x, off_y = self.map_manager.lobby_offset
        return (
            off_x + global_map_settings.zone_width // 2,
            off_y + global_map_settings.zone_height // 2,
        )

    def update(self, clock, screen, camera):
        self.ecs_world.update(camera)

    def render(self, screen, camera):
        self.ecs_world.render(screen, camera)
