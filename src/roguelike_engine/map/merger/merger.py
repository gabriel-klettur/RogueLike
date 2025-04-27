# src.roguelike_project/map/map_merger.py

import random

from roguelike_engine.map.utils import find_closest_room_center
from roguelike_engine.map.generator.dungeon_generator import create_horizontal_tunnel, create_vertical_tunnel

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
        print("⚠️ Forzando salida central en borde inferior del lobby.")
        mid_x = len(lobby_map[0]) // 2
        ensure_lobby_exit_at(lobby_map, mid_x, len(lobby_map) - 1)
        exit_pos = (offset_x + mid_x, offset_y + len(lobby_map) - 1)

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

def ensure_lobby_exit_at(lobby_map, x, y):
    print(f"🧱 Forzando salida en lobby en coordenada relativa ({x}, {y})")
    row = list(lobby_map[y])
    row[x] = "."
    lobby_map[y] = "".join(row)
