# Path: src/roguelike_game/ecs/fsm/states/spell/prepare_spell_state.py
from roguelike_game.ecs.systems.fsm.state import State
import time

from roguelike_game.config.spells_config import SPELLS

class PrepareSpellState(State):
    def enter(self, entity):
        # Iniciar temporizador de preparación
        self.fsm.context['prepare_start'] = time.time()

    def execute(self, entity, dt):
        # Duración dinámica de preparación según el hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('prepare_duration', 0)
        # Penalización x multiplicador si es auto-cast
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        if time.time() - self.fsm.context['prepare_start'] >= duration:
            from roguelike_game.ecs.systems.fsm.states.spell.channel_spell_state import ChannelSpellState
            self.fsm.change_state(ChannelSpellState(), entity)

    def exit(self, entity):
        pass