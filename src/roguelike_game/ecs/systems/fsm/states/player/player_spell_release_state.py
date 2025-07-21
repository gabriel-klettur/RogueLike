from roguelike_game.ecs.systems.fsm.states.spell.release_spell_state import ReleaseSpellState

class PlayerSpellReleaseState(ReleaseSpellState):
    """Wrapper para lanzamiento de hechizo del jugador."""
    def enter(self, entity):
        super().enter(entity)

    def execute(self, entity, dt):
        super().execute(entity, dt)

    def exit(self, entity):
        super().exit(entity)