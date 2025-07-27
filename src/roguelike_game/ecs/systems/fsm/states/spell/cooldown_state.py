from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState
import time
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent

class CooldownState(State):
    def enter(self, entity):

        # Iniciar temporizador de cooldown
        start = time.time()
        self.fsm.context['cooldown_start'] = start
        # Crear barra de hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=start, active=True, state='cooldown')
        
    def execute(self, entity, dt):
        # Duración de cooldown dinámica según el hechizo y punish por automatic
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        if time.time() - self.fsm.context.get('cooldown_start', 0) >= duration:
            self.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        # Desactivar barra de hechizo al salir de cooldown
        comps = entity.world.components.get('MagicSpellBarComponent', {})
        comp = comps.get(entity.id)
        if comp:
            comp.active = False
        pass