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
        base = SPELLS.get(spell, {}).get('channel_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        if time.time() - self.fsm.context['channel_start'] >= duration:
            self.fsm.change_state(ReleaseSpellState(), entity)

    def exit(self, entity):
        pass