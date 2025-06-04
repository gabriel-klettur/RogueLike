from roguelike_game.ecs.fsm.state import State
import time
from roguelike_game.ecs.fsm.states.spell.channel_spell_state import ChannelSpellState
from roguelike_game.config.spells_config import SPELLS

class PrepareSpellState(State):
    def enter(self, entity):
        # Iniciar temporizador de preparación
        self.fsm.context['prepare_start'] = time.time()

    def execute(self, entity, dt):
        # Duración dinámica de preparación según el hechizo
        spell = self.fsm.context.get('spell')
        duration = SPELLS.get(spell, {}).get('prepare_duration', 0)
        if time.time() - self.fsm.context['prepare_start'] >= duration:
            self.fsm.change_state(ChannelSpellState(), entity)

    def exit(self, entity):
        pass