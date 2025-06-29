# Path: src/roguelike_game/ecs/fsm/states/player/player_spell_cast_state.py
from roguelike_game.ecs.fsm.states.cast_state import CastState

class PlayerSpellCastState(CastState):
    """Wrapper para iniciar el casting de hechizo para el jugador."""
    def enter(self, entity):
        super().enter(entity)

    def execute(self, entity, dt):
        super().execute(entity, dt)

    def exit(self, entity):
        super().exit(entity)