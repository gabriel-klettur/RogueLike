# Path: src/roguelike_game/entities/load_hostile.py
import random
from roguelike_game.entities.npc.factory import NPCFactory
from roguelike_engine.config.config_tiles import TILE_SIZE

def load_hostile(rooms, player_start_tile, dungeon_offset, tile_map):
    """
    Genera enemigos dentro de la dungeon, garantizando spawn solo en tiles transitables,
    y que toda la hitbox quede dentro de la room donde spawnea el NPC.
    :param rooms: lista de tuplas (x1,y1,x2,y2) en coords de tiles locales.
    :param player_start_tile: (tile_x, tile_y) en coords globales.
    :param dungeon_offset: (offset_x_tiles, offset_y_tiles).
    :param tile_map: matriz [filas][cols] de objetos Tile para todo el mapa.
    :return: lista de entidades NPC.
    """
    enemies = []
    off_x, off_y = dungeon_offset

    # Lookup de tiles por coordenada (col, row)
    tile_lookup = {
        (tile.x // TILE_SIZE, tile.y // TILE_SIZE): tile
        for row in tile_map for tile in row
    }
    # Tiles transitables
    dungeon_passable = {
        coord for coord, tile in tile_lookup.items() if not tile.solid
    }

    def room_pixel_rect(room):
        x1, y1, x2, y2 = room
        left   = (x1 + off_x) * TILE_SIZE
        top    = (y1 + off_y) * TILE_SIZE
        right  = (x2 + off_x + 1) * TILE_SIZE
        bottom = (y2 + off_y + 1) * TILE_SIZE
        return left, top, right, bottom

    def try_spawn(npc_type, room, attempts=20, shift_left_px=0):
        """
        Intenta spawnear un NPC de tipo `npc_type` dentro de `room` probando posiciones
        aleatorias hasta que su hitbox quede 100% dentro de la habitación.
        """
        room_rect = room_pixel_rect(room)
        for _ in range(attempts):
            # tile aleatorio dentro de la room
            x1, y1, x2, y2 = room
            gx = random.randint(x1 + off_x, x2 + off_x)
            gy = random.randint(y1 + off_y, y2 + off_y)
            if (gx, gy) not in dungeon_passable:
                continue

            npc = NPCFactory.create(npc_type, x=0, y=0)
            # posicionamos según hitbox_offset y posible shift
            tile_px_x = gx * TILE_SIZE
            tile_px_y = gy * TILE_SIZE
            m = npc.model
            offbx = getattr(m, 'hitbox_offset_x', 0) + shift_left_px
            offby = getattr(m, 'hitbox_offset_y', 0)
            m.x = tile_px_x - offbx
            m.y = tile_px_y - offby

            # ✅ aquí usamos npc.movement.hitbox, no model.hitbox
            hit = npc.movement.hitbox(m.x, m.y)

            left, top, right, bottom = room_rect
            if (hit.left   >= left   and
                hit.top    >= top    and
                hit.right  <= right  and
                hit.bottom <= bottom):
                return npc

        # Fallback en el centro de la room
        cx = (room[0] + room[2]) // 2 + off_x
        cy = (room[1] + room[3]) // 2 + off_y
        npc = NPCFactory.create(npc_type, x=0, y=0)
        m = npc.model
        offbx = getattr(m, 'hitbox_offset_x', 0) + shift_left_px
        offby = getattr(m, 'hitbox_offset_y', 0)
        m.x = cx * TILE_SIZE - offbx
        m.y = cy * TILE_SIZE - offby
        return npc

    # 1) Spawn 1–2 monstruos por sala
    for room in rooms:
        for _ in range(random.randint(1, 2)):
            monster = try_spawn("monster", room)
            enemies.append(monster)

    if not rooms:
        return enemies

    # 2) Sala más lejana al jugador
    def manh(c):
        return abs(c[0] - player_start_tile[0]) + abs(c[1] - player_start_tile[1])
    far_room = max(rooms, key=lambda r: manh(((r[0]+r[2])//2+off_x, (r[1]+r[3])//2+off_y)))

    # 3) Spawn extra de 1–2 monstruos en la sala más lejana
    for _ in range(random.randint(1, 2)):
        monster = try_spawn("monster", far_room)
        enemies.append(monster)

    # 4) Spawn 1 élite, desplazada 8px a la izquierda
    elite = try_spawn("elite", far_room, shift_left_px=8)
    enemies.append(elite)

    return enemies