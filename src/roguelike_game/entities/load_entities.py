# Path: src/roguelike_game/entities/load_entities.py
from roguelike_game.entities.player.controller.player_controller import PlayerController
from roguelike_game.entities.load_obstacles import load_obstacles
from roguelike_game.entities.load_buildings import load_buildings
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.entities.player.config_player import RENDERED_SPRITE_SIZE

def load_entities(z_state=None):
    obstacles = load_obstacles()
    lobby_off = global_map_settings.lobby_offset
    tile_cx = lobby_off[0] + global_map_settings.zone_size[0] // 2
    tile_cy = lobby_off[1] + global_map_settings.zone_size[1] // 2
    spawn_x = tile_cx * TILE_SIZE - RENDERED_SPRITE_SIZE[0] // 2
    spawn_y = tile_cy * TILE_SIZE - RENDERED_SPRITE_SIZE[1] // 2
    player_ctrl = PlayerController(spawn_x, spawn_y, z_state, obstacles=obstacles)
    buildings   = load_buildings(z_state)
    
    return player_ctrl, obstacles, buildings