# Path: src/roguelike_engine/map/utils.py
from typing import Tuple, List
from roguelike_engine.config.map_config import global_map_settings
def intersect(room1, room2):
    x1a, y1a, x2a, y2a = room1
    x1b, y1b, x2b, y2b = room2
    return (
        x1a <= x2b and x2a >= x1b and
        y1a <= y2b and y2a >= y1b
    )

def center_of(room):
    x1, y1, x2, y2 = room
    return (x1 + x2) // 2, (y1 + y2) // 2

def find_closest_room_center(source_x, source_y, dungeon_rooms):
    print(f"🔍 Buscando sala más cercana desde ({source_x}, {source_y})")
    min_dist = float("inf")
    closest_center = None
    for i, room in enumerate(dungeon_rooms):
        cx, cy = center_of(room)
        dist = abs(cx - source_x) + abs(cy - source_y)
        print(f"  🧭 Sala {i}: centro=({cx},{cy}), dist={dist}")
        if dist < min_dist:
            min_dist = dist
            closest_center = (cx, cy)
    return closest_center


def get_zone_for_tile(tile_x: int, tile_y: int) -> str:
    """
    Devuelve el nombre de zona en la que cae la tile (tile_x,tile_y),
    comparando contra ZONE_OFFSETS y ZONE_SIZE.
    """
    w, h = global_map_settings.zone_size
    for zone, (ox, oy) in global_map_settings.zone_offsets.items():
        if ox <= tile_x < ox + w and oy <= tile_y < oy + h:
            return zone
    return "no zone"

def generate_lobby_matrix() -> List[str]:
    """
    Genera dinámicamente el mapa del lobby de tamaño ZONE_WIDTH×ZONE_HEIGHT:
    - Borde de muros '#'
    - Interior de suelo '.'
    """
    matrix: List[str] = []
    for y in range(global_map_settings.zone_height):
        if y == 0 or y == global_map_settings.zone_height - 1:
            matrix.append("#" * global_map_settings.zone_width)
        else:
            matrix.append("#" + "." * (global_map_settings.zone_width - 2) + "#")
    return matrix


def find_lobby_exit(lobby: List[str], side: str) -> Tuple[int, int]:
    """
    Encuentra un punto de salida en el lobby según el lado indicado:
      - 'bottom', 'top', 'left', 'right'
    """
    h = len(lobby)
    w = len(lobby[0])
    if side == "bottom":
        for x in range(w):
            if lobby[h-1][x] == '.':
                return x, h-1
        return w//2, h-1
    if side == "top":
        for x in range(w):
            if lobby[0][x] == '.':
                return x, 0
        return w//2, 0
    if side == "left":
        for y in range(h):
            if lobby[y][0] == '.':
                return 0, y
        return 0, h//2
    # 'right'
    for y in range(h):
        if lobby[y][w-1] == '.':
            return w-1, y
    return w-1, h//2


def calculate_lobby_offset() -> Tuple[int, int]:
    """
    Determina el offset (x,y) para centrar el lobby en la celda central de un grid.
    """
    n_cols = global_map_settings.global_width // global_map_settings.zone_width
    n_rows = global_map_settings.global_height // global_map_settings.zone_height
    if n_cols < 1 or n_rows < 1:
        return ((global_map_settings.global_width - global_map_settings.zone_width)//2,
                (global_map_settings.global_height - global_map_settings.zone_height)//2)
    center_col = n_cols // 2
    center_row = n_rows // 2
    rem_x = global_map_settings.global_width - n_cols * global_map_settings.zone_width
    rem_y = global_map_settings.global_height - n_rows * global_map_settings.zone_height
    start_x = rem_x // 2
    start_y = rem_y // 2
    return (start_x + center_col * global_map_settings.zone_width,
            start_y + center_row * global_map_settings.zone_height)



def calculate_dungeon_offset(lobby_off: Tuple[int,int]):
    """
    Calcula el offset (x,y) para colocar la dungeon adyacente al lobby
    según DUNGEON_CONNECT_SIDE: arriba, abajo, izquierda, derecha.
    """
    off_x, off_y = lobby_off
    if global_map_settings.dungeon_connect_side == "bottom":
        return off_x, off_y + global_map_settings.zone_height
    if global_map_settings.dungeon_connect_side == "top":
        return off_x, off_y - global_map_settings.zone_height
    if global_map_settings.dungeon_connect_side == "left":
        return off_x - global_map_settings.zone_width, off_y
    # 'right'
    return off_x + global_map_settings.zone_width, off_y
