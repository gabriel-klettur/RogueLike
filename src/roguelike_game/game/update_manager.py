from roguelike_engine.utils.benchmark import benchmark
import pygame
import types
from roguelike_engine.config.config_tiles import TILE_SIZE

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
        eid = ecs.npc_world.player_entity
        pos = ecs.npc_world.components['Position'][eid]
        camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))
        return

    # 2) Si el Buildings-Editor está activo, solo actualizamos él
    if buildings_editor.editor_state.active:
        @benchmark(perf_log, "2.0.2.buildings_editor.update")
        def _update_buildings_editor():
            buildings_editor.update(camera)
        _update_buildings_editor()
        # Centrar cámara en el jugador incluso con editor activo
        eid = ecs.npc_world.player_entity
        pos = ecs.npc_world.components['Position'][eid]
        camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))
        return

    # 3.1) Cámara sigue al jugador
    @benchmark(perf_log, "2.1.camera.update")
    def _update_camera():
        # Centrar cámara usando la posición del jugador en ECS
        eid = ecs.npc_world.player_entity
        pos = ecs.npc_world.components['Position'][eid]
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
        eid = ecs.npc_world.player_entity
        pos = ecs.npc_world.components['Position'][eid]
        minimap.update(
            player_pos=(pos.x, pos.y),
            tiles=map.tiles_in_region
        )
    _update_minimap()
