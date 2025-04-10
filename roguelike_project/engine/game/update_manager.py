# Path: roguelike_project/engine/game/update_manager.py

def update_game(state):
    """Actualiza el estado general del juego."""
    if not state.running:
        return

    # Actualizar la cámara con base en la posición del jugador
    state.camera.update(state.player)
    
    # Calcular los tiles sólidos y combinar enemigos locales y remotos
    solid_tiles = [tile for tile in state.tiles if tile.solid]
    enemies = state.enemies + list(state.remote_entities.values())

    # Actualizar el jugador (incluye explosiones, proyectiles, láseres, etc.)
    state.player.update(solid_tiles, enemies)
    
    # Actualizar enemigos
    for enemy in enemies:
        enemy.update()
    
    # Limpiar la lista de proyectiles que han terminado su ciclo
    state.player.projectiles = [p for p in state.player.projectiles if p.alive]
