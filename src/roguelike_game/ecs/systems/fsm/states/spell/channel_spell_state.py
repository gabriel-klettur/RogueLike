# Path: src/roguelike_game/ecs/fsm/states/spell/channel_spell_state.py
from roguelike_game.ecs.systems.fsm.state import State
import time

from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent

class ChannelSpellState(State):
    def enter(self, entity):
        self.fsm.context['channel_start'] = time.time()
        # Crear barra de hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('channel_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=self.fsm.context['channel_start'], active=True, state='channel')

    def execute(self, entity, dt):
        # Dinámica: duración de canalización según hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('channel_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        if time.time() - self.fsm.context['channel_start'] >= duration:
            from roguelike_game.ecs.systems.fsm.states.spell.release_spell_state import ReleaseSpellState
            self.fsm.change_state(ReleaseSpellState(), entity)

    def exit(self, entity):
        # Desactivar barra de hechizo al salir de canalización
        comps = entity.world.components.get('MagicSpellBarComponent', {})
        comp = comps.get(entity.id)
        if comp:
            comp.active = False