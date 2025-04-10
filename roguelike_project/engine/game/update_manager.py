# roguelike_project/engine/game/update_manager.py

def update_game(state):
    """Actualiza el estado general del juego."""
    if not state.running:
        return

    # Actualizar la cámara con base en la posición del jugador
    state.camera.update(state.player)

    # ✅ CombatSystem ahora se encarga de todo el combate
    state.player.combat.update(state)

    # Actualizar enemigos
    enemies = state.enemies + list(state.remote_entities.values())
    for enemy in enemies:
        enemy.update()

    # (Ya no necesitamos limpiar proyectiles aquí)
