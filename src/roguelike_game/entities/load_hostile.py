import random
from roguelike_game.entities.npc.factory import NPCFactory
from src.roguelike_engine.config_tiles import TILE_SIZE

def load_hostile(rooms, player_start_tile, dungeon_offset):
    """
    Genera enemigos procedurales dentro de las habitaciones:
    - Cada monstruo se coloca en una posición aleatoria dentro de la sala.
    - En la sala más lejana al jugador, además 1–2 monstruos y 1 elite.
    :param rooms: lista de tuplas (x1, y1, x2, y2) en coords locales (tiles).
    :param player_start_tile: (tile_x, tile_y) del jugador en coords globales (tiles).
    :param dungeon_offset: (offset_x, offset_y) en tiles donde empieza la dungeon.
    :return: lista de entidades NPC con coords globales (píxeles).
    """
    enemies = []
    off_x, off_y = dungeon_offset

    def random_in_room(room):
        # Elige un tile válido dentro de la sala (evita muros)
        min_x, min_y, max_x, max_y = room
        if max_x - min_x > 2 and max_y - min_y > 2:
            tx = random.randint(min_x + 1, max_x - 1)
            ty = random.randint(min_y + 1, max_y - 1)
        else:
            # Si la sala es muy pequeña, usa el centro
            tx = (min_x + max_x) // 2
            ty = (min_y + max_y) // 2
        # Convertir a coords globales en píxeles
        return (off_x + tx) * TILE_SIZE, (off_y + ty) * TILE_SIZE

    # 1️⃣ Spawn de 1–2 monstruos por sala
    for room in rooms:
        for _ in range(random.randint(1, 2)):
            x_px, y_px = random_in_room(room)
            enemies.append(NPCFactory.create("monster", x=x_px, y=y_px))

    if not rooms:
        return enemies

    # 2️⃣ Sala más lejana (Manhattan) al jugador
    def manhattan_center(room):
        cx = (room[0] + room[2]) // 2 + off_x
        cy = (room[1] + room[3]) // 2 + off_y
        return abs(cx - player_start_tile[0]) + abs(cy - player_start_tile[1])

    far_room = max(rooms, key=manhattan_center)

    # 3️⃣ Spawn adicional en la sala más lejana: 1–2 monsters + 1 elite
    for _ in range(random.randint(1, 2)):
        x_px, y_px = random_in_room(far_room)
        enemies.append(NPCFactory.create("monster", x=x_px, y=y_px))
    x_px, y_px = random_in_room(far_room)
    enemies.append(NPCFactory.create("elite", x=x_px, y=y_px))

    return enemies
