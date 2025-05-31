from roguelike_game.ecs.fsm.state import State

class GhostState(State):
    """
    Estado Ghost: el jugador atraviesa paredes y NPCs lo ignoran.
    """
    def enter(self, entity):
        world = entity.world
        eid = entity.id
        world.components.setdefault('IsGhost', {})[eid] = True

    def execute(self, entity, dt):
        # No necesita lógica por tick
        pass

    def exit(self, entity):
        world = entity.world
        eid = entity.id
        world.components.get('IsGhost', {}).pop(eid, None)
