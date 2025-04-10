# roguelike_project/engine/game/update_manager.py

def update_game(state):
    """Actualiza el estado general del juego."""
    if not state.running:
        return

    # Actualizar la cámara con base en la posición del jugador
    state.camera.update(state.player)

    # Calcular los tiles sólidos y combinar enemigos locales y remotos
    solid_tiles = [tile for tile in state.tiles if tile.solid]
    enemies = state.enemies + list(state.remote_entities.values())

    # ✅ Nuevo: actualizar combate desde sistema global
    state.combat.update()

    # Actualizar enemigos
    for enemy in enemies:
        enemy.update()

    # Limpiar proyectiles muertos ya lo hace `combat.update()`, así que no se repite aquí
