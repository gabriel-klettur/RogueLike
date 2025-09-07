from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.config.spells_config import SPELLS
import time
import pygame
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent

import logging
logger = logging.getLogger(__name__)


class PlayerSpellCooldownState(State):
    """Wrapper para cooldown de hechizo del jugador."""
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()
        # Crear barra de cooldown
        start = self.fsm.context['cooldown_start']
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=start, active=True, state='cooldown')

    def execute(self, entity, dt):
        # Duración de cooldown con penalización si automatic
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        elapsed = time.time() - self.fsm.context.get('cooldown_start', 0)
        if elapsed >= duration:
            # Debug del cooldown
            spell_key = self.fsm.context.get('spell', '')
            logger.debug(f" Eid={entity.id} state PlayerSpellCooldownState -> IdleState (cooldown {elapsed:.2f}s spell={spell_key})")
            # Recast automático: si automatic y botón sigue presionado, reiniciar la sub-FSM a PrepareSpellState
            if self.fsm.context.get('automatic', False) and pygame.mouse.get_pressed()[0]:
                # Verificar que hay maná suficiente ANTES de recastear
                try:
                    world = entity.world
                    mana_comp = world.components.get('Mana', {}).get(entity.id)
                    mana_cost = float(SPELLS.get(spell_key, {}).get('mana_cost', 0))
                    cur = float(getattr(mana_comp, 'current_mana', 0)) if mana_comp is not None else 0.0
                except Exception:
                    mana_cost = 0.0
                    cur = 0.0
                if mana_cost > 0 and cur < mana_cost:
                    # Feedback no intrusivo y antispam (1s)
                    now = time.time()
                    last_warn = float(self.fsm.context.get('_no_mana_warn_ts', 0.0))
                    if now - last_warn > 1.0:
                        try:
                            from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
                            push_bubble(entity.world, entity.id, 'No tengo suficiente maná', color=(240, 200, 200), ttl_ms=1200)
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
                        self.fsm.context['_no_mana_warn_ts'] = now
                    # Reintentar pronto sin spamear recasteo: espera corta
                    retry_wait = 0.2
                    self.fsm.context['cooldown_start'] = now - (duration - retry_wait)
                    return
                # Suficiente maná: proceder a preparar siguiente cast
                from roguelike_game.ecs.systems.fsm.states.spell.prepare_spell_state import PrepareSpellState
                self.fsm.change_state(PrepareSpellState(), entity)
                return
            # No recast: salir a estado global IdleState
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No requiere limpieza adicional
        pass