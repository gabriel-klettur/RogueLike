from roguelike_engine.utils.benchmark import benchmark
import pygame
import types
from roguelike_engine.config.config_tiles import TILE_SIZE
import time
from roguelike_engine.map.events.events import handle_expand_dungeon, _next_zone_key
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.core.spatial_index import SpatialIndex

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

    # 3.1) Cámara sigue al jugador
    @benchmark(perf_log, "2.1.camera.update")
    def _update_camera():
        # Centrar cámara usando la posición del jugador en ECS
        eid = ecs.ecs_world.player_entity
        pos = ecs.ecs_world.components['Position'][eid]
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
        # Usar posición del jugador en ECS
        eid = ecs.ecs_world.player_entity
        pos = ecs.ecs_world.components['Position'][eid]
        minimap.update(
            player_pos=(pos.x, pos.y),
            tiles=map.tiles_in_region
        )
    _update_minimap()

    # 4) Detectar estancia en área 3x3 para expandir dungeon (solo si existe dungeon inicial)
    if map.rooms and not hasattr(state, 'expand_area_coords'):
        # Calcular centro de la sala más lejana al lobby y offset de dungeon
        lob_x, lob_y = map.lobby_offset
        dun_x, dun_y = map.dungeon_offset
        zone_w, zone_h = global_map_settings.zone_width, global_map_settings.zone_height
        center_lobby = (lob_x + zone_w//2, lob_y + zone_h//2)
        max_dist = -1; far_center = None
        for r in map.rooms:
            cx_rel = (r[0] + r[2]) // 2; cy_rel = (r[1] + r[3]) // 2
            cx = dun_x + cx_rel; cy = dun_y + cy_rel
            d = abs(cx - center_lobby[0]) + abs(cy - center_lobby[1])
            if d > max_dist:
                max_dist, far_center = d, (cx, cy)
        state.expand_area_coords = [(far_center[0] + dx, far_center[1] + dy)
                                    for dx in (-1,0,1) for dy in (-1,0,1)]
        state.expand_area_start_time = None
        state.last_area_print_time = None  # Init print timer
        print(f"[ExpansionTrigger] Coords iniciales: {state.expand_area_coords}")  # Debug inicialización

    # Verificar estancia en área de expansión si se definió
    if hasattr(state, 'expand_area_coords'):
        px, py = entities.player.x, entities.player.y
        tx, ty = int(px)//TILE_SIZE, int(py)//TILE_SIZE
        inside = (tx, ty) in state.expand_area_coords
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
                # Recalcular área para nueva zona
                off_x, off_y = global_map_settings.zone_offsets[new_key]
                zone_w, zone_h = global_map_settings.zone_width, global_map_settings.zone_height
                new_center = (off_x + zone_w//2, off_y + zone_h//2)
                state.expand_area_coords = [(new_center[0]+dx, new_center[1]+dy)
                                            for dx in (-1,0,1) for dy in (-1,0,1)]
                print(f"[ExpansionTrigger] Nueva área coords: {state.expand_area_coords}")
                # Reset timers
                state.expand_area_start_time = None
                state.last_area_print_time = None
        else:
            # Fuera del área: reset timers
            state.expand_area_start_time = None
            state.last_area_print_time = None
