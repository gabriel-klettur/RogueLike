from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.monster.aggro_state import AggroState
import time
from roguelike_game.config.spells_config import SPELLS

class CooldownState(State):
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()

    def execute(self, entity, dt):
        # Duración de cooldown dinámica según el hechizo y punish por automatic
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        if time.time() - self.fsm.context.get('cooldown_start', 0) >= duration:
            self.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        pass