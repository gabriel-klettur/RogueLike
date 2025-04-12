def update_game(state):
    if not state.running:
        return

    state.camera.update(state.player)

    enemies = state.enemies + list(state.remote_entities.values())    
    for enemy in enemies:
        enemy.update()

    state.player.movement.update_dash(
        [t for t in state.tiles if t.solid],
        state.obstacles
    )

    state.combat.update()

    state.effects.update()
