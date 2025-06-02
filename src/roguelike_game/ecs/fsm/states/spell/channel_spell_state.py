from roguelike_game.ecs.fsm.state import State
import time
from roguelike_game.ecs.fsm.states.spell.release_spell_state import ReleaseSpellState
from roguelike_game.config.spells_config import SPELLS

class ChannelSpellState(State):
    def enter(self, entity):
        self.fsm.context['channel_start'] = time.time()

    def execute(self, entity, dt):
        # Dinámica: duración de canalización según hechizo
        spell = self.fsm.context.get('spell')
        duration = SPELLS.get(spell, {}).get('channel_duration', 0)
        if time.time() - self.fsm.context['channel_start'] >= duration:
            self.fsm.change_state(ReleaseSpellState(), entity)

    def exit(self, entity):
        pass