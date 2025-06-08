from roguelike_engine.utils.benchmark import benchmark
import pygame
import types
from roguelike_engine.config.config_tiles import TILE_SIZE
import time
from roguelike_engine.map.events.events import handle_expand_dungeon, _next_zone_key
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.core.spatial_index import SpatialIndex
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from collections import deque

def update_game(
    state,
    systems,
    camera,
    clock,
    screen,
    map,
    entities,      
    tiles_editor,
    buildings_editor,
    map_editor,
    minimap,
    ecs,
    perf_log
):
    """
    Actualiza el juego en cada frame, incluyendo:
      1) Lógica de editores (tiles/buildings)
      2) Mecánicas core: cámara, sistemas, enemigos, jugador...
    """
    if not state.running:
        return

    # 1) Prioridad: si el Tile-Editor está activo, nada más se hace
    if tiles_editor.editor_state.active:
        @benchmark(perf_log, "2.0.1.tiles_editor.update")
        def _update_tiles_editor():
            tiles_editor.update(camera, map)
        _update_tiles_editor()
        # Centrar cámara en el jugador incluso con editor activo
        eid = ecs.ecs_world.player_entity
        pos = ecs.ecs_world.components['Position'][eid]
        camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))
        return

    # 2) Si el Buildings-Editor está activo, solo actualizamos él
    if buildings_editor.editor_state.active:
        @benchmark(perf_log, "2.0.2.buildings_editor.update")
        def _update_buildings_editor():
            buildings_editor.update(camera)
        _update_buildings_editor()
        # Centrar cámara en el jugador incluso con editor activo
        eid = ecs.ecs_world.player_entity
        pos = ecs.ecs_world.components['Position'][eid]
        camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))
        return

    # 3) Si el Map-Editor está activo, solo actualizamos él
    if map_editor.editor_state.active:
        @benchmark(perf_log, "2.0.3.map_editor.update")
        def _update_map_editor():
            map_editor.update(camera, map)
        _update_map_editor()
        # Free camera panning with arrow keys
        keys = pygame.key.get_pressed()
        dx = int(keys[pygame.K_RIGHT]) - int(keys[pygame.K_LEFT])
        dy = int(keys[pygame.K_DOWN]) - int(keys[pygame.K_UP])
        # Base speed and speed boost with Shift
        base_speed = 10
        shift = keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]
        pan_speed = base_speed * (3 if shift else 1)
        camera.offset_x += dx * pan_speed
        camera.offset_y += dy * pan_speed
        return

    # 3.1) Cámara sigue al jugador si está vivo (tiene Position)
    @benchmark(perf_log, "2.1.camera.update")
    def _update_camera():
        eid = ecs.ecs_world.player_entity
        pos_map = ecs.ecs_world.components.get('Position', {})
        if eid in pos_map:
            pos = pos_map[eid]
            camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))
    _update_camera()

    # 3.2) Sistemas principales
    @benchmark(perf_log, "2.2.systems.update")
    def _update_systems():
        systems.update(clock, screen)
    _update_systems()

    # 3.3) Todas las entidades
    @benchmark(perf_log, "2.3.entities.update")
    def _update_entities():
        entities.update(state, map, systems, perf_log)
    _update_entities()

    # 3.4) Minimap update
    @benchmark(perf_log, "2.5.minimap.update")
    def _update_minimap():
        # Actualizar minimapa solo si el jugador todavía existe (tiene Position)
        eid = ecs.ecs_world.player_entity
        pos_map = ecs.ecs_world.components.get('Position', {})
        if eid in pos_map:
            pos = pos_map[eid]
            minimap.update(
                player_pos=(pos.x, pos.y),
                tiles=map.tiles_in_region
            )
    _update_minimap()

    # 4) Detectar estancia en área 3x3 para expandir dungeon (solo si existe dungeon inicial)
    if map.rooms and not hasattr(state, 'expand_area_coords'):
        # BFS path para sala más lejana desde lobby
        # Construir walkable tiles
        walkable = set()
        for row in map.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE; ty = tile.y // TILE_SIZE
                if not getattr(tile, "solid", False):
                    walkable.add((tx, ty))
        # Origen: centro del lobby en tiles
        lob_x, lob_y = map.lobby_offset
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
        dun_x, dun_y = map.dungeon_offset
        for r in map.rooms:
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
        print(f"[ExpansionTrigger] Coords iniciales: {state.expand_area_coords}")  # Debug inicialización

    # Verificar estancia en área de expansión si se definió
    if hasattr(state, 'expand_area_coords'):
        # Detect NPCs and player via their colliders: if any collider overlaps el área roja
        inside = False
        pos_map = ecs.ecs_world.components['Position']
        for eid in ecs.ecs_world.get_entities_with('MultiCollider','Position'):
            multi = ecs.ecs_world.components['MultiCollider'][eid]
            pos = pos_map[eid]
            for collider in multi.colliders.values():
                # construir rect del collider sea cual sea su tipo
                rect = build_collider_rect(pos.x, pos.y, collider)
                x1, x2 = rect.left // TILE_SIZE, rect.right // TILE_SIZE
                y1, y2 = rect.top  // TILE_SIZE, rect.bottom // TILE_SIZE
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
                print(f"[ExpansionTrigger] Dentro área, tiempo transcurrido: {elapsed:.2f}s")
                state.last_area_print_time = now
            # Control de temporizador de expansión
            if state.expand_area_start_time is None:
                state.expand_area_start_time = now
            elif now - state.expand_area_start_time >= 3.0:
                # Trigger expansion como F3 y mover área roja a nuevo dungeon
                new_key, parent_key = _next_zone_key()
                fake_evt = types.SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F3)
                handle_expand_dungeon(fake_evt, map, entities)
                # Actualizar índice espacial tras expansión para colisiones
                ecs.ecs_world.spatial_index = SpatialIndex(map, entities.buildings)
                # Recalcular área de expansión usando ruta más larga desde lobby
                # Construir conjunto de tiles caminables
                walkable = set()
                for row in map.tiles:
                    for tile in row:
                        tx = tile.x // TILE_SIZE; ty = tile.y // TILE_SIZE
                        if not getattr(tile, "solid", False):
                            walkable.add((tx, ty))
                # Origen BFS: centro del lobby
                lob_x, lob_y = map.lobby_offset
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
                for zkey, rooms in map.zone_rooms.items():
                    off_zx, off_zy = global_map_settings.zone_offsets[zkey]
                    for r in rooms:
                        cx_rel = (r[0]+r[2])//2; cy_rel = (r[1]+r[3])//2
                        c = (off_zx + cx_rel, off_zy + cy_rel)
                        d = dist.get(c)
                        if d is not None and d > max_d:
                            max_d, far_center = d, c
                state.expand_area_coords = [(far_center[0]+dx, far_center[1]+dy)
                                            for dx in (-1,0,1) for dy in (-1,0,1)]
                print(f"[ExpansionTrigger] Nueva área coords (path): {state.expand_area_coords}")
        else:
            # Fuera del área: reset timers
            state.expand_area_start_time = None
            state.last_area_print_time = None
