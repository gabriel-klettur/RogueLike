
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
    minimap
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
    enemies = entities.enemies
    from roguelike_game.entities.npc.types.elite.controller import EliteController
    for enemy in enemies:
        # Si enemy es un NPC (tiene .controller), usamos .controller, si no, es el controller mismo
        ctrl = getattr(enemy, 'controller', enemy)
        if isinstance(ctrl, EliteController):
            ctrl.update(state, map, entities, systems.effects, systems.explosions)
        else:
            enemy.update(state, map, entities)

    # ————— Movimiento especial del jugador —————
    # (por ejemplo, dash con colisiones)
    solid_tiles = [t for t in map.tiles_in_region if t.solid]
    entities.player.movement.update_dash(
        solid_tiles,
        entities.obstacles
    )


    # ————— Actualizar minimapa —————
    minimap.update(
        player_pos=(entities.player.x, entities.player.y),
        tiles=map.tiles_in_region
    )
# Path: src/roguelike_game/game/update_manager.py