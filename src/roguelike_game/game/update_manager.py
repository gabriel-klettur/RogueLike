
def update_game(state, systems, camera, clock, screen, map, entities):
    if not state.running:
        return

    camera.update(entities.player)

    systems.update(clock, screen)

    #!------------------ ESTO DEBERIAMOS MEJORARLO ------------------
    enemies = entities.enemies + list(state.remote_entities.values())    
    for enemy in enemies:
        enemy.update(state, map, entities)

    entities.player.movement.update_dash(
        [t for t in map.tiles_in_region if t.solid],
        entities.obstacles
    )

    #!---------------------------------------------------------------

    
# Path: src/roguelike_game/game/update_manager.py