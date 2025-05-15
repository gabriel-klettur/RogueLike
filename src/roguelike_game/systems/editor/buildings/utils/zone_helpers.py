from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_map import ZONE_OFFSETS
from roguelike_engine.map.utils import get_zone_for_tile

def assign_zone_and_relatives(building) -> None:
    """
    Asigna a `building` su zona y coordenadas relativas en tiles.

    - Calcula el punto central del sprite en píxeles.
    - Lo convierte a coordenadas de tile.
    - Determina la zona usando get_zone_for_tile().
    - Calcula rel_tile_x / rel_tile_y = tile_x - zone_offset.
    - Si no hay zona válida, deja zone y rel_tile_* a None.

    Modifica in-place los atributos:
    building.zone: str | None
    building.rel_tile_x: int | None
    building.rel_tile_y: int | None
    """
    # 1) Centro del sprite en píxeles
    w_px, h_px = building.image.get_size()
    cx_px = building.x + w_px / 2
    cy_px = building.y + h_px / 2

    # 2) A coordenadas de tile
    tile_x = int(cx_px) // TILE_SIZE
    tile_y = int(cy_px) // TILE_SIZE

    print(f"🧭 Centro del sprite en píxeles: ({cx_px}, {cy_px}) -> tile ({tile_x}, {tile_y})")
    # 3) Detectar zona
    
    zone = get_zone_for_tile(tile_x, tile_y)
    print(f"Zona detectada: {zone} para tile ({tile_x},{tile_y})")


    # 4) Calcular coordenadas relativas
    ox, oy = ZONE_OFFSETS.get(zone, (0, 0))
    rel_x = tile_x - ox
    rel_y = tile_y - oy

    # 5) Asignar
    building.zone = zone
    building.rel_tile_x = rel_x
    building.rel_tile_y = rel_y