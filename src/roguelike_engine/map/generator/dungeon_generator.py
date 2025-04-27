# src.roguelike_project/map/dungeon_generator.py

import random
from src.roguelike_engine.config import DUNGEON_WIDTH, DUNGEON_HEIGHT
from roguelike_engine.map.utils import intersect, center_of

def generate_dungeon_map(
    width=DUNGEON_WIDTH,
    height=DUNGEON_HEIGHT,
    max_rooms=10,
    room_min_size=10,
    room_max_size=20,
    return_rooms=False,
    avoid_zone=None
):
    print("📦 Generando dungeon procedural...")
    map_ = [["#" for _ in range(width)] for _ in range(height)]
    rooms = []

    for i in range(max_rooms):
        w = random.randint(room_min_size, room_max_size)
        h = random.randint(room_min_size, room_max_size)
        x = random.randint(1, width - w - 1)
        y = random.randint(1, height - h - 1)
        new_room = (x, y, x + w, y + h)

        # Validar colisión con otras habitaciones
        if any(intersect(room, new_room) for room in rooms):
            print(f"⛔ Habitación {i} descartada por colisión")
            continue

        # Validar colisión con zona protegida (conexión)
        if avoid_zone:
            zx1, zy1, zx2, zy2 = avoid_zone
            x1, y1, x2, y2 = new_room
            if not (x2 < zx1 or x1 > zx2 or y2 < zy1 or y1 > zy2):
                print(f"⛔ Habitación {i} bloqueada por zona de conexión segura")
                continue

        print(f"✅ Habitación {i} aceptada en: {new_room}")
        for i_ in range(y, y + h):
            for j in range(x, x + w):
                map_[i_][j] = "O"

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
    return (map_, rooms) if return_rooms else map_

def create_horizontal_tunnel(map_, x1, x2, y):
    for x in range(min(x1, x2), max(x1, x2) + 1):
        map_[y][x] = "="

def create_vertical_tunnel(map_, y1, y2, x):
    for y in range(min(y1, y2), max(y1, y2) + 1):
        map_[y][x] = "="
