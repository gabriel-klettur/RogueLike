from collections import deque
import time
import types
import pygame

from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.events.events import handle_expand_dungeon, _next_zone_key
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.core.spatial_index import SpatialIndex
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

import logging
logger = logging.getLogger(__name__)

import logging
logger = logging.getLogger(__name__)

class ExpansionSystem:
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2. expansion.update")
    def update(self, world, camera):
        state = world.state
        game_map = world.map_manager
        entities_manager = world.entities_manager
        # 1) Inicialización de área de expansión (solo si existe dungeon inicial)
        if game_map.rooms and not hasattr(state, 'expand_area_coords'):
            # BFS path para sala más lejana desde lobby
            # Construir walkable tiles
            walkable = set()
            for row in game_map.tiles:
                for tile in row:
                    tx = tile.x // TILE_SIZE; ty = tile.y // TILE_SIZE
                    if not getattr(tile, "solid", False):
                        walkable.add((tx, ty))
            # Origen: centro del lobby en tiles
            lob_x, lob_y = game_map.lobby_offset
            zone_w, zone_h = global_map_settings.zone_width, global_map_settings.zone_height
            origin = (lob_x + zone_w//2, lob_y + zone_h//2)
            # BFS distancias
            dist = {origin: 0}
            dq = deque([origin])
            while dq:
                x, y = dq.popleft()
                for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                    if (nx, ny) in walkable and (nx, ny) not in dist:
                        dist[(nx, ny)] = dist[(x, y)] + 1
                        dq.append((nx, ny))
            # Seleccionar room con distancia máxima
            far_center, max_d = origin, -1
            dun_x, dun_y = game_map.dungeon_offset
            for r in game_map.rooms:
                cx_rel = (r[0] + r[2]) // 2; cy_rel = (r[1] + r[3]) // 2
                c = (dun_x + cx_rel, dun_y + cy_rel)
                d = dist.get(c)
                if d is not None and d > max_d:
                    max_d, far_center = d, c
            # Default si no hay ruta
            if max_d < 0:
                far_center = origin
            state.expand_area_coords = [(far_center[0] + dx, far_center[1] + dy)
                                        for dx in (-1,0,1) for dy in (-1,0,1)]
            state.expand_area_start_time = None
            state.last_area_print_time = None  # Init print timer
            logger.debug(f"[ExpansionTrigger] Coords iniciales: {state.expand_area_coords}")

        # 2) Detección y trigger de expansión
        if hasattr(state, 'expand_area_coords'):
            # Detect NPCs y player via sus colliders: si algún collider overlap el área roja
            inside = False
            pos_map = world.components['Position']
            for eid in world.get_entities_with('MultiCollider','Position'):
                multi = world.components['MultiCollider'][eid]
                pos = pos_map[eid]
                for collider in multi.colliders.values():
                    rect = build_collider_rect(pos.x, pos.y, collider)
                    x1, x2 = rect.left // TILE_SIZE, rect.right // TILE_SIZE
                    y1, y2 = rect.top // TILE_SIZE, rect.bottom // TILE_SIZE
                    for cx in range(x1, x2+1):
                        for cy in range(y1, y2+1):
                            if (cx, cy) in state.expand_area_coords:
                                inside = True
                                break
                        if inside: break
                    if inside: break
                if inside: break
            if inside:
                now = time.time()
                # Print once per second
                if state.last_area_print_time is None or now - state.last_area_print_time >= 1.0:
                    elapsed = (now - state.expand_area_start_time) if state.expand_area_start_time else 0.0
                    logger.debug(f"[ExpansionTrigger] Dentro área, tiempo transcurrido: {elapsed:.2f}s")
                    state.last_area_print_time = now
                # Control de temporizador de expansión
                if state.expand_area_start_time is None:
                    state.expand_area_start_time = now
                elif now - state.expand_area_start_time >= 3.0:
                    # Trigger expansion como F3 y mover área roja a nuevo dungeon
                    new_key, parent_key = _next_zone_key()
                    fake_evt = types.SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F3)
                    handle_expand_dungeon(fake_evt, game_map, entities_manager)
                    # Actualizar índice espacial tras expansión para colisiones
                    world.spatial_index = SpatialIndex(game_map, entities_manager.buildings)
                    # Recalcular área de expansión usando ruta más larga desde lobby
                    # Construir conjunto de tiles caminables
                    walkable = set()
                    for row in game_map.tiles:
                        for tile in row:
                            tx = tile.x // TILE_SIZE; ty = tile.y // TILE_SIZE
                            if not getattr(tile, "solid", False):
                                walkable.add((tx, ty))
                    # Origen BFS: centro del lobby
                    lob_x, lob_y = game_map.lobby_offset
                    zone_w, zone_h = global_map_settings.zone_width, global_map_settings.zone_height
                    origin = (lob_x + zone_w//2, lob_y + zone_h//2)
                    # BFS para distancias
                    dist = {origin: 0}
                    dq = deque([origin])
                    while dq:
                        x, y = dq.popleft()
                        for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                            if (nx, ny) in walkable and (nx, ny) not in dist:
                                dist[(nx, ny)] = dist[(x, y)] + 1
                                dq.append((nx, ny))
                    # Seleccionar centro de room con distancia máxima
                    far_center = origin; max_d = -1
                    for zkey, rooms in game_map.zone_rooms.items():
                        off_zx, off_zy = global_map_settings.zone_offsets[zkey]
                        for r in rooms:
                            cx_rel = (r[0]+r[2])//2; cy_rel = (r[1]+r[3])//2
                            c = (off_zx + cx_rel, off_zy + cy_rel)
                            d = dist.get(c)
                            if d is not None and d > max_d:
                                max_d, far_center = d, c
                    state.expand_area_coords = [(far_center[0]+dx, far_center[1]+dy)
                                                for dx in (-1,0,1) for dy in (-1,0,1)]
                    logger.debug(f"[ExpansionTrigger] Nueva área coords (path): {state.expand_area_coords}")
            else:
                # Fuera del área: reset timers
                state.expand_area_start_time = None
                state.last_area_print_time = None
