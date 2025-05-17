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
        return

    # 2) Si el Buildings-Editor está activo, solo actualizamos él
    if buildings_editor.editor_state.active:
        @benchmark(perf_log, "2.0.buildings_editor.update")
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

    # 3.3) IA enemigos (throttle y agrupación)
    @benchmark(perf_log, "2.3.enemies.update")
    def _update_enemies():
        # Throttle: actualizar IA cada 2 frames (opcional)
        tick = getattr(state, "_enemy_tick", 0) + 1
        state._enemy_tick = tick
        if tick % 2 != 0:
            return

        px, py = entities.player.x, entities.player.y        
        max_dist_sq = ENEMY_MAX_UPDATE_DISTANCE * ENEMY_MAX_UPDATE_DISTANCE

        normals = []
        elites = []
        for enemy in entities.enemies:
            # calcular vector relativo y su longitud al cuadrado
            dx = enemy.x - px
            dy = enemy.y - py
            if dx*dx + dy*dy > max_dist_sq:
                # está demasiado lejos: no actualizamos IA
                continue

            ctrl = getattr(enemy, "controller", None)
            if isinstance(ctrl, EliteController):
                elites.append(ctrl)
            else:
                normals.append(enemy)

        # Ahora sí, actualizamos solo los cercanos
        for e in normals:
            e.update(state, map, entities)
        for ctrl in elites:
            ctrl.update(state, map, entities, systems.effects, systems.explosions)
    _update_enemies()


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
