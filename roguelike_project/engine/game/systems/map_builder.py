# roguelike_project/engine/game/systems/map_builder.py

from roguelike_project.config import DUNGEON_WIDTH, DUNGEON_HEIGHT, LOBBY_OFFSET_X, LOBBY_OFFSET_Y, LOBBY_WIDTH, LOBBY_HEIGHT

from roguelike_project.map.dungeon_generator import generate_dungeon_map
from roguelike_project.map.map_merger import merge_handmade_with_generated
from roguelike_project.map.handmade_maps.lobby_map import LOBBY_MAP
from roguelike_project.map.tile_loader import load_map_from_text
from roguelike_project.map.map_exporter import save_map_with_autoname


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
        # Zona reservada para evitar generar salas debajo del lobby
        avoid_zone = (
            offset_x,
            offset_y + LOBBY_HEIGHT,
            offset_x + LOBBY_WIDTH,
            offset_y + LOBBY_HEIGHT + 3
        )

        dungeon_map, dungeon_rooms = generate_dungeon_map(width, height, return_rooms=True, avoid_zone=avoid_zone)

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
        save_map_with_autoname(merged_map)

    else:
        raise ValueError(f"❌ Modo de mapa no reconocido: {map_mode}")

    tiles = load_map_from_text(merged_map)
    return merged_map, tiles


