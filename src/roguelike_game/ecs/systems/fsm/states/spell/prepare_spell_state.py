from roguelike_game.ecs.systems.fsm.state import State
import time

from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent

class PrepareSpellState(State):
    def enter(self, entity):
        # Iniciar temporizador de preparación
        self.fsm.context['prepare_start'] = time.time()
        # Crear barra de hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('prepare_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=self.fsm.context['prepare_start'], active=True, state='prepare')

    def execute(self, entity, dt):
        # Duración dinámica de preparación según el hechizo
        spell = self.fsm.context.get('spell')
        base = SPELLS.get(spell, {}).get('prepare_duration', 0)
        # Penalización x multiplicador si es auto-cast
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        # Guard: suficiente maná para proceder hacia Channel/Release
        try:
            world = entity.world
            mana_comp = world.components.get('Mana', {}).get(entity.id)
            mana_cost = float(SPELLS.get(spell, {}).get('mana_cost', 0))
            cur = float(getattr(mana_comp, 'current_mana', 0)) if mana_comp is not None else 0.0
        except Exception:
            mana_cost = 0.0
            cur = 0.0
        if mana_cost > 0 and cur < mana_cost:
            # Feedback antispam cada 1s
            now = time.time()
            last_warn = float(self.fsm.context.get('_no_mana_warn_prepare_ts', 0.0))
            if now - last_warn > 1.0:
                try:
                    from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
                    push_bubble(entity.world, entity.id, 'No tengo suficiente maná', color=(240, 200, 200), ttl_ms=1000)
                except Exception:
                    pass
                try:
                    flash_store = getattr(world, '_mana_flash_until', None)
                    if not isinstance(flash_store, dict):
                        flash_store = {}
                        setattr(world, '_mana_flash_until', flash_store)
                    import time as _time
                    flash_store[entity.id] = _time.time() + 0.5
                except Exception:
                    pass
                self.fsm.context['_no_mana_warn_prepare_ts'] = now
            # Reiniciar temporizador de preparación para seguir esperando maná
            self.fsm.context['prepare_start'] = now
            return
        if time.time() - self.fsm.context['prepare_start'] >= duration:
            from roguelike_game.ecs.systems.fsm.states.spell.channel_spell_state import ChannelSpellState
            self.fsm.change_state(ChannelSpellState(), entity)

    def exit(self, entity):
        # Desactivar barra de hechizo al salir de preparación
        comps = entity.world.components.get('MagicSpellBarComponent', {})
        comp = comps.get(entity.id)
        if comp:
            comp.active = False