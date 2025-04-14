# roguelike_project/map/map_generator.py

import random
from roguelike_project.config import DUNGEON_WIDTH, DUNGEON_HEIGHT

from roguelike_project.map.geometry import intersect, center_of, find_closest_room_center


def generate_dungeon_map(width=DUNGEON_WIDTH, height=DUNGEON_HEIGHT, max_rooms=10, room_min_size=10, room_max_size=20, return_rooms=False):
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

def create_horizontal_tunnel(map_, x1, x2, y):
    print(f"🛠️  Crear pasillo horizontal en Y={y}, de X={x1} a X={x2}")
    for x in range(min(x1, x2), max(x1, x2) + 1):
        map_[y][x] = "."

def create_vertical_tunnel(map_, y1, y2, x):
    print(f"🛠️  Crear pasillo vertical en X={x}, de Y={y1} a Y={y2}")
    for y in range(min(y1, y2), max(y1, y2) + 1):
        map_[y][x] = "."



