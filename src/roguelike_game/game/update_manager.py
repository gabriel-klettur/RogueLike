
def update_game(state, systems, camera, clock, screen):
    if not state.running:
        return

    camera.update(state.player)

    systems.update(clock, screen)

    #!------------------ ESTO DEBERIAMOS MEJORARLO ------------------
    enemies = state.enemies + list(state.remote_entities.values())    
    for enemy in enemies:
        enemy.update(state)

    state.player.movement.update_dash(
        [t for t in state.tiles if t.solid],
        state.obstacles
    )

    #!---------------------------------------------------------------

    
# Path: src/roguelike_game/game/update_manager.py