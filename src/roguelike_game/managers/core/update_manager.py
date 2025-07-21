from roguelike_engine.utils.benchmark import benchmark
import pygame
import types



def update_game(
    state,    
    camera,
    clock,
    screen,
    map,
    buildings,      
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
    # Pausar update si ItemEditor visible
    if getattr(state, 'item_editor_state', None) and state.item_editor_state.visible:
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

    # 3.3) Todas las entidades
    @benchmark(perf_log, "2.3.entities.update")
    def _update_entities():
        buildings.update(state, map, perf_log)
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
