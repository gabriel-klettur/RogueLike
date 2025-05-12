# Path: src/roguelike_game/game/update_manager.py

def update_game(
    state,
    systems,
    camera,
    clock,
    screen,
    map,
    entities,
    network,    
    tiles_editor,
    buildings_editor
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
        buildings_editor.update(camera)
        return

    # 3) Flujo normal de juego
    # ————— Cámara sigue al jugador —————
    camera.update(entities.player)

    # ————— Sistemas (combat, efectos, explosiones...) —————
    systems.update(clock, screen)

    # ————— IA de enemigos (incluye remotos) —————
    enemies = entities.enemies + list(network.remote_entities.values())
    for enemy in enemies:
        enemy.update(state, map, entities)

    # ————— Movimiento especial del jugador —————
    # (por ejemplo, dash con colisiones)
    solid_tiles = [t for t in map.tiles_in_region if t.solid]
    entities.player.movement.update_dash(
        solid_tiles,
        entities.obstacles
    )
