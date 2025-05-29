from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
import time
from roguelike_game.config.spells_config import SPELLS

cfg = SPELLS['pixel_fire']
COOLDOWN_DURATION = cfg['cooldown_duration']  # segundos de enfriamiento

class CooldownState(State):
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()

    def execute(self, entity, dt):
        # Avanzar a AggroState cuando acabe cooldown
        if time.time() - self.fsm.context.get('cooldown_start', 0) >= COOLDOWN_DURATION:
            self.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        pass