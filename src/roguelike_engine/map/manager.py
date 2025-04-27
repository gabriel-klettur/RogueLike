# src.roguelike_project/engine/game/systems/map_manager.py

from src.roguelike_engine.config import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
    LOBBY_WIDTH,
    LOBBY_HEIGHT
)
from roguelike_engine.map.generator.dungeon_generator import generate_dungeon_map
from roguelike_engine.map.merger.merger import merge_handmade_with_generated
from data.maps.handmade_maps.lobby_map import LOBBY_MAP
from src.roguelike_engine.map.loader.tile_loader import load_map_from_text
from roguelike_engine.map.exporter.map_exporter import save_map_with_autoname
from roguelike_engine.map.overlay.overlay_manager import load_overlay

def build_map(
    width=DUNGEON_WIDTH,
    height=DUNGEON_HEIGHT,
    offset_x=LOBBY_OFFSET_X,
    offset_y=LOBBY_OFFSET_Y,
    map_mode="combined",
    merge_mode="center_to_center",
    map_name: str = None
):
    """
    Construye el mapa y su capa overlay “permanente”:
      - Si map_name es None, usamos 'lobby_map' para modo lobby,
        o 'combined_map' para modo combined.
      - Generamos la dungeon y la combinamos, pero **SIEMPRE**
        cargamos (y luego guardamos) el overlay desde map_name.overlay.json.
    Devuelve:
      merged_map, tiles, overlay_map, key
    donde `key` = map_name, la clave para persistir overlay.
    """
    # 0) Elegir clave base (no cambiamos tras cada save)
    if map_name is None:
        map_name = "lobby_map" if map_mode == "lobby" else "combined_map"

    # 1) Generar el mapa según modo
    if map_mode == "lobby":
        merged_map = LOBBY_MAP

    elif map_mode == "dungeon":
        dungeon_map = generate_dungeon_map(width, height)
        merged_map = ["".join(row) for row in dungeon_map]

    elif map_mode == "combined":
        # Genera dungeon dejando espacio para el lobby
        avoid_zone = (
            offset_x,
            offset_y + LOBBY_HEIGHT,
            offset_x + LOBBY_WIDTH,
            offset_y + LOBBY_HEIGHT + 3
        )
        dungeon_map, dungeon_rooms = generate_dungeon_map(
            width, height, return_rooms=True, avoid_zone=avoid_zone
        )
        if offset_x + LOBBY_WIDTH > width or offset_y + LOBBY_HEIGHT > height:
            raise ValueError("❌ El lobby no cabe en el mapa generado.")

        merged_map = merge_handmade_with_generated(
            LOBBY_MAP,
            dungeon_map,
            offset_x=offset_x,
            offset_y=offset_y,
            merge_mode=merge_mode,
            dungeon_rooms=dungeon_rooms
        )
        # OPCIONAL: guardar un .txt de debug, pero no lo usamos para overlay
        _ = save_map_with_autoname(merged_map)

    else:
        raise ValueError(f"❌ Modo de mapa no reconocido: {map_mode}")

    # 2) Cargar overlay **siempre** desde map_name.overlay.json
    overlay_map = load_overlay(map_name)

    # 3) Crear Tiles
    tiles = load_map_from_text(merged_map, overlay_map)

    # 4) Devolver también la clave estática para futuros saves
    return merged_map, tiles, overlay_map, map_name
