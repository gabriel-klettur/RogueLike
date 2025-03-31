import random

MAP_WIDTH = 80
MAP_HEIGHT = 45

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

def create_h_corridor(map_, x1, x2, y):
    for x in range(min(x1, x2), max(x1, x2) + 1):
        map_[y][x] = "."

def create_v_corridor(map_, y1, y2, x):
    for y in range(min(y1, y2), max(y1, y2) + 1):
        map_[y][x] = "."

def generate_dungeon_map(width=MAP_WIDTH, height=MAP_HEIGHT, max_rooms=10, room_min_size=5, room_max_size=10):
    map_ = [["#" for _ in range(width)] for _ in range(height)]
    rooms = []

    for _ in range(max_rooms):
        w = random.randint(room_min_size, room_max_size)
        h = random.randint(room_min_size, room_max_size)
        x = random.randint(1, width - w - 1)
        y = random.randint(1, height - h - 1)

        new_room = (x, y, x + w, y + h)

        if any(intersect(room, new_room) for room in rooms):
            continue

        # Dibujar habitación
        for i in range(y, y + h):
            for j in range(x, x + w):
                map_[i][j] = "."

        # Conectar con la habitación anterior
        if rooms:
            prev_x, prev_y = center_of(rooms[-1])
            new_x, new_y = center_of(new_room)

            if random.random() < 0.5:
                create_h_corridor(map_, prev_x, new_x, prev_y)
                create_v_corridor(map_, prev_y, new_y, new_x)
            else:
                create_v_corridor(map_, prev_y, new_y, prev_x)
                create_h_corridor(map_, prev_x, new_x, new_y)

        rooms.append(new_room)

    return map_

def merge_handmade_with_generated(handmade_map, generated_map, offset_x=0, offset_y=0):
    # Copiar el mapa generado
    new_map = [list(row) for row in generated_map]

    for y, row in enumerate(handmade_map):
        for x, char in enumerate(row):
            map_x = x + offset_x
            map_y = y + offset_y
            if 0 <= map_y < len(new_map) and 0 <= map_x < len(new_map[0]):
                new_map[map_y][map_x] = char

    return ["".join(row) for row in new_map]
