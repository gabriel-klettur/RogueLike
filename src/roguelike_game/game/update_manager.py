from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.entities.npc.types.elite.controller import EliteController
from roguelike_game.config_entities import ENEMY_MAX_UPDATE_DISTANCE

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
        return

    # 2) Si el Buildings-Editor está activo, solo actualizamos él
    if buildings_editor.editor_state.active:
        @benchmark(perf_log, "2.0.2.buildings_editor.update")
        def _update_buildings_editor():
            buildings_editor.update(camera)
        _update_buildings_editor()
        return

    # 3.1) Cámara sigue al jugador
    @benchmark(perf_log, "2.1.camera.update")
    def _update_camera():
        camera.update(entities.player)
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


    # 3.4) Movimiento especial del jugador
    @benchmark(perf_log, "2.4.player.update_dash")
    def _update_dash():
        solid_tiles = [t for t in map.tiles_in_region if t.solid]
        entities.player.movement.update_dash(solid_tiles, entities.obstacles)
    _update_dash()

    # 3.5) Minimap update
    @benchmark(perf_log, "2.5.minimap.update")
    def _update_minimap():
        minimap.update(
            player_pos=(entities.player.x, entities.player.y),
            tiles=map.tiles_in_region
        )
    _update_minimap()
