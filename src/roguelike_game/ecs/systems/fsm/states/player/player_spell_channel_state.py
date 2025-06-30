# Path: src/roguelike_game/ecs/fsm/states/player/player_spell_channel_state.py
from roguelike_game.ecs.systems.fsm.states.spell.channel_spell_state import ChannelSpellState

class PlayerSpellChannelState(ChannelSpellState):
    """Wrapper para canalización de hechizo del jugador."""
    def enter(self, entity):
        super().enter(entity)

    def execute(self, entity, dt):
        super().execute(entity, dt)

    def exit(self, entity):
        super().exit(entity)