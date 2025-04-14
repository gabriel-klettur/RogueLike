# roguelike_project/map/map_generator.py

import random
from roguelike_project.config import DUNGEON_WIDTH, DUNGEON_HEIGHT

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

def create_horizontal_tunnel(map_, x1, x2, y):
    print(f"🛠️  Crear pasillo horizontal en Y={y}, de X={x1} a X={x2}")
    for x in range(min(x1, x2), max(x1, x2) + 1):
        map_[y][x] = "."

def create_vertical_tunnel(map_, y1, y2, x):
    print(f"🛠️  Crear pasillo vertical en X={x}, de Y={y1} a Y={y2}")
    for y in range(min(y1, y2), max(y1, y2) + 1):
        map_[y][x] = "."

def generate_dungeon_map(width=DUNGEON_WIDTH, height=DUNGEON_HEIGHT, max_rooms=10, room_min_size=5, room_max_size=10, return_rooms=False):
    print("📦 Generando dungeon procedural...")
    map_ = [["#" for _ in range(width)] for _ in range(height)]
    rooms = []

    for i in range(max_rooms):
        w = random.randint(room_min_size, room_max_size)
        h = random.randint(room_min_size, room_max_size)
        x = random.randint(1, width - w - 1)
        y = random.randint(1, height - h - 1)

        new_room = (x, y, x + w, y + h)
        if any(intersect(room, new_room) for room in rooms):
            print(f"⛔ Habitación {i} descartada por colisión")
            continue

        print(f"✅ Habitación {i} aceptada en: {new_room}")
        for i_ in range(y, y + h):
            for j in range(x, x + w):
                map_[i_][j] = "."

        if rooms:
            prev_x, prev_y = center_of(rooms[-1])
            new_x, new_y = center_of(new_room)
            if random.random() < 0.5:
                create_horizontal_tunnel(map_, prev_x, new_x, prev_y)
                create_vertical_tunnel(map_, prev_y, new_y, new_x)
            else:
                create_vertical_tunnel(map_, prev_y, new_y, prev_x)
                create_horizontal_tunnel(map_, prev_x, new_x, new_y)

        rooms.append(new_room)

    print(f"🏰 Total habitaciones generadas: {len(rooms)}")
    if return_rooms:
        return map_, rooms
    return map_

def merge_handmade_with_generated(handmade_map, generated_map, offset_x=0, offset_y=0, merge_mode="center_to_center", dungeon_rooms=None):
    print("🔀 Iniciando merge del lobby con dungeon...")
    new_map = [list(row) for row in generated_map]

    print(f"📍 Offset aplicado al lobby: ({offset_x}, {offset_y})")
    print(f"📐 Tamaño lobby: {len(handmade_map[0])}x{len(handmade_map)}")

    for y, row in enumerate(handmade_map):
        for x, char in enumerate(row):
            map_x = x + offset_x
            map_y = y + offset_y
            if 0 <= map_y < len(new_map) and 0 <= map_x < len(new_map[0]):
                new_map[map_y][map_x] = char

    if merge_mode == "center_to_center" and dungeon_rooms:
        print("🔧 Aplicando modo de conexión: center_to_center")
        connect_from_lobby_exit(
            new_map, handmade_map, offset_x, offset_y, dungeon_rooms
        )
    else:
        print("ℹ️ No se aplicó ninguna conexión automática (merge_mode o rooms vacíos).")

    return ["".join(row) for row in new_map]

def connect_from_lobby_exit(map_, lobby_map, offset_x, offset_y, dungeon_rooms):
    print("📡 Buscando punto de salida del lobby...")
    exit_pos = find_exit_from_lobby(lobby_map, offset_x, offset_y)
    if not exit_pos:
        print("❌ No se encontró salida en el lobby.")
        return

    exit_x, exit_y = exit_pos
    print(f"🚪 Punto de salida del lobby: ({exit_x}, {exit_y})")

    target_x, target_y = find_closest_room_center(exit_x, exit_y, dungeon_rooms)
    print(f"🎯 Sala destino más cercana: ({target_x}, {target_y})")

    if random.random() < 0.5:
        print("📏 Trayectoria: Horizontal ➝ Vertical")
        create_horizontal_tunnel(map_, exit_x, target_x, exit_y)
        create_vertical_tunnel(map_, exit_y, target_y, target_x)
    else:
        print("📏 Trayectoria: Vertical ➝ Horizontal")
        create_vertical_tunnel(map_, exit_y, target_y, exit_x)
        create_horizontal_tunnel(map_, exit_x, target_x, target_y)

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

def find_exit_from_lobby(lobby_map, offset_x, offset_y):
    print("🔍 Buscando un '.' en el borde inferior del lobby...")
    height = len(lobby_map)
    width = len(lobby_map[0])
    
    for x in range(width):
        if lobby_map[height - 1][x] == ".":
            print(f"✅ Salida encontrada en borde inferior: ({x}, {height - 1})")
            return offset_x + x, offset_y + height - 1

    print("🔍 Buscando un '.' en los bordes laterales del lobby...")
    for y in range(height):
        if lobby_map[y][0] == ".":
            print(f"✅ Salida izquierda: (0, {y})")
            return offset_x + 0, offset_y + y
        if lobby_map[y][width - 1] == ".":
            print(f"✅ Salida derecha: ({width - 1}, {y})")
            return offset_x + width - 1, offset_y + y

    print("⚠️ No se encontró una salida válida en el lobby.")
    return None
