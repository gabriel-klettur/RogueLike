# roguelike_project/engine/game/update_manager.py

def update_game(state):
    """Actualiza el estado general del juego."""
    if not state.running:
        return

    # Actualizar la cámara con base en la posición del jugador
    state.camera.update(state.player)

    # Actualizar los enemigos y los enemigos remotos    
    enemies = state.enemies + list(state.remote_entities.values())    
    for enemy in enemies:
        enemy.update()

    # Actualizamos las animaciones de combate
    state.combat.update()
    
