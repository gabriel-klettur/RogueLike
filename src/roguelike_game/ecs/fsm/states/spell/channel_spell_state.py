from roguelike_game.ecs.fsm.state import State
import time
from roguelike_game.ecs.fsm.states.spell.release_spell_state import ReleaseSpellState
from roguelike_game.config.spells_config import SPELLS

cfg = SPELLS['fireball']

CHANNEL_DURATION = cfg['channel_duration']  # segundos de canalización

class ChannelSpellState(State):
    def enter(self, entity):
        self.fsm.context['channel_start'] = time.time()

    def execute(self, entity, dt):
        # Aquí podrías reproducir animación de canalización
        if time.time() - self.fsm.context['channel_start'] >= CHANNEL_DURATION:
            self.fsm.change_state(ReleaseSpellState(), entity)

    def exit(self, entity):
        pass