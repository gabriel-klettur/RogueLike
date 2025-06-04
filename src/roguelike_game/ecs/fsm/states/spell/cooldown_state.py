from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
import time
from roguelike_game.config.spells_config import SPELLS

class CooldownState(State):
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()

    def execute(self, entity, dt):
        # Duración de cooldown dinámica según el hechizo actual
        spell_key = self.fsm.context.get('spell')
        duration = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        if time.time() - self.fsm.context.get('cooldown_start', 0) >= duration:
            self.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        pass