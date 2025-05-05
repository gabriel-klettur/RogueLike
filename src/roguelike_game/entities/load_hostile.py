# Path: src/roguelike_game/entities/load_hostile.py

import random
from roguelike_game.entities.npc.factory import NPCFactory
from src.roguelike_engine.config_tiles import TILE_SIZE


def load_hostile(rooms, player_start_tile, dungeon_offset, tile_map):
    """
    Genera enemigos dentro de la dungeon, garantizando spawn solo en tiles transitables
    y alineando la posición al hitbox de los pies.
    :param rooms: lista de tuplas (x1,y1,x2,y2) en coords de tiles locales.
    :param player_start_tile: (tile_x, tile_y) en coords globales.
    :param dungeon_offset: (offset_x_tiles, offset_y_tiles).
    :param tile_map: matriz [filas][cols] de objetos Tile para todo el mapa.
    :return: lista de entidades NPC.
    """
    enemies = []
    off_x, off_y = dungeon_offset

    # Construir lookup de tiles por coordenada (col, row)
    tile_lookup = {
        (tile.x // TILE_SIZE, tile.y // TILE_SIZE): tile
        for row in tile_map for tile in row
    }

    # Lista global de tiles no sólidos de la dungeon
    dungeon_passable = [
        (gx, gy) for (gx, gy), tile in tile_lookup.items()
        if not tile.solid
    ]

    # Función auxiliar para centrar entidad en hitbox de pies
    def adjust_to_hitbox(npc_entity, gx, gy):
        # Coordenadas del tile en píxeles
        tile_x = gx * TILE_SIZE
        tile_y = gy * TILE_SIZE
        # Ajustar de modo que el origen (sprite top-left) coloque el hitbox de pies en tile
        model = npc_entity.model
        offset_x = getattr(model, 'hitbox_offset_x', 0)
        offset_y = getattr(model, 'hitbox_offset_y', 0)
        model.x = tile_x - offset_x
        model.y = tile_y - offset_y
        return npc_entity

    # Calcular centros de cada sala en coords de tile
    centers = [
        ((x1 + x2) // 2 + off_x, (y1 + y2) // 2 + off_y)
        for x1, y1, x2, y2 in rooms
    ]

    # 1️⃣ Spawn de 1–2 monsters por sala, centrados en hitbox
    for cx, cy in centers:
        for _ in range(random.randint(1, 2)):
            gx, gy = (cx, cy) if (cx, cy) in dungeon_passable else random.choice(dungeon_passable)
            npc = NPCFactory.create("monster", x=0, y=0)
            adjust_to_hitbox(npc, gx, gy)
            enemies.append(npc)

    if not centers:
        return enemies

    # Sala más lejana al jugador (Manhattan)
    def manhattan(c):
        return abs(c[0] - player_start_tile[0]) + abs(c[1] - player_start_tile[1])

    far_cx, far_cy = max(centers, key=manhattan)

    # 2️⃣ Spawn extra de 1–2 monsters en la sala más lejana
    for _ in range(random.randint(1, 2)):
        gx, gy = (far_cx, far_cy) if (far_cx, far_cy) in dungeon_passable else random.choice(dungeon_passable)
        npc = NPCFactory.create("monster", x=0, y=0)
        adjust_to_hitbox(npc, gx, gy)
        enemies.append(npc)

    # 3️⃣ Un elite
    gx, gy = (far_cx, far_cy) if (far_cx, far_cy) in dungeon_passable else random.choice(dungeon_passable)
    elite = NPCFactory.create("elite", x=0, y=0)
    adjust_to_hitbox(elite, gx, gy)
    enemies.append(elite)

    return enemies
