from roguelike_project.map.map_generator import generate_dungeon_map, merge_handmade_with_generated
from roguelike_project.map.lobby_map import LOBBY_MAP
from roguelike_project.map.map_loader import load_map_from_text

def build_map(width=60, height=40, offset_x=5, offset_y=5):
    dungeon = generate_dungeon_map(width, height)
    merged_map = merge_handmade_with_generated(LOBBY_MAP, dungeon, offset_x, offset_y)
    tiles = load_map_from_text(merged_map)
    return merged_map, tiles
