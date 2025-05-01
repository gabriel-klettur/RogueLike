# Path: src/roguelike_engine/map/utils.py
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
