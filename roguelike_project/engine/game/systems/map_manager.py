# roguelike_project/engine/game/systems/map_manager.py

from roguelike_project.map.map_generator import generate_dungeon_map, merge_handmade_with_generated
from roguelike_project.map.lobby_map import LOBBY_MAP
from roguelike_project.map.map_loader import load_map_from_text
from roguelike_project.config import DUNGEON_WIDTH, DUNGEON_HEIGHT, LOBBY_OFFSET_X, LOBBY_OFFSET_Y, LOBBY_WIDTH, LOBBY_HEIGHT

def build_map(
    width=DUNGEON_WIDTH,
    height=DUNGEON_HEIGHT,
    offset_x=LOBBY_OFFSET_X,
    offset_y=LOBBY_OFFSET_Y,
    map_mode="combined",
    merge_mode="center_to_center"
):
    if map_mode == "lobby":
        merged_map = LOBBY_MAP

    elif map_mode == "dungeon":
        dungeon_map = generate_dungeon_map(width, height)
        merged_map = ["".join(row) for row in dungeon_map]

    elif map_mode == "combined":
        dungeon_map, dungeon_rooms = generate_dungeon_map(width, height, return_rooms=True)

        # Validación para asegurarse de que el lobby entra en el mapa
        if offset_x + LOBBY_WIDTH > width or offset_y + LOBBY_HEIGHT > height:
            raise ValueError("❌ El lobby no cabe en el mapa generado. Ajusta el offset o el tamaño del dungeon.")

        merged_map = merge_handmade_with_generated(
            LOBBY_MAP,
            dungeon_map,
            offset_x=offset_x,
            offset_y=offset_y,
            merge_mode=merge_mode,
            dungeon_rooms=dungeon_rooms
        )

    else:
        raise ValueError(f"❌ Modo de mapa no reconocido: {map_mode}")

    tiles = load_map_from_text(merged_map)
    return merged_map, tiles
