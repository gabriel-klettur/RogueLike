from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_game.config.spells_config import SPELLS
import time

class PlayerSpellCooldownState(State):
    """Wrapper para cooldown de hechizo del jugador."""
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()

    def execute(self, entity, dt):
        # Duración de cooldown según hechizo actual
        spell_key = self.fsm.context.get('spell')
        duration = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        if time.time() - self.fsm.context.get('cooldown_start', 0) >= duration:
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No requiere limpieza adicional
        pass