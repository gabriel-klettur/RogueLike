from roguelike_game.ecs.fsm.state import State
import time
from roguelike_game.ecs.fsm.states.spell.channel_spell_state import ChannelSpellState
from roguelike_game.config.spells_config import SPELLS

cfg = SPELLS['fireball']
PREPARE_DURATION = cfg['prepare_duration']

class PrepareSpellState(State):
    def enter(self, entity):
        # Iniciar temporizador de preparación
        self.fsm.context['prepare_start'] = time.time()

    def execute(self, entity, dt):
        if time.time() - self.fsm.context['prepare_start'] >= PREPARE_DURATION:
            self.fsm.change_state(ChannelSpellState(), entity)

    def exit(self, entity):
        pass