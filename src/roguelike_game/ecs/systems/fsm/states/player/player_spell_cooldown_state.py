from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.config.spells_config import SPELLS
import time
import pygame
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent
from roguelike_game.config.input_config import InputConfig

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
            # Recast automático: si automatic y botón sigue presionado (mouse/teclado/mando), reiniciar a PrepareSpellState
            def _gp_pressed(token: str | None) -> bool:
                if not token:
                    return False
                t = str(token).upper()
                try:
                    pygame.joystick.init()
                except Exception:
                    pass
                BTN_INDEX = {
                    'G_BTN_A': 0, 'G_BTN_B': 1, 'G_BTN_X': 2, 'G_BTN_Y': 3,
                    'G_LB': 4, 'G_RB': 5, 'G_BACK': 6, 'G_START': 7, 'G_GUIDE': 8,
                    'G_LS': 9, 'G_RS': 10,
                }
                try:
                    for i in range(pygame.joystick.get_count()):
                        js = pygame.joystick.Joystick(i)
                        try:
                            js.init()
                        except Exception:
                            pass
                        if t in BTN_INDEX:
                            idx = BTN_INDEX[t]
                            try:
                                if js.get_button(idx):
                                    return True
                            except Exception:
                                pass
                        if t.startswith('G_DPAD_'):
                            try:
                                vx, vy = js.get_hat(0)
                            except Exception:
                                vx, vy = (0, 0)
                            if t == 'G_DPAD_UP' and vy == 1: return True
                            if t == 'G_DPAD_DOWN' and vy == -1: return True
                            if t == 'G_DPAD_LEFT' and vx == -1: return True
                            if t == 'G_DPAD_RIGHT' and vx == 1: return True
                        # Axes (digitalizados)
                        ax = None; sign = 0; thresh = 0.5
                        if t == 'G_AXIS_LX_POS': ax, sign = 0, +1
                        elif t == 'G_AXIS_LX_NEG': ax, sign = 0, -1
                        elif t == 'G_AXIS_LY_POS': ax, sign = 1, +1
                        elif t == 'G_AXIS_LY_NEG': ax, sign = 1, -1
                        elif t == 'G_AXIS_RX_POS': ax, sign = 2, +1
                        elif t == 'G_AXIS_RX_NEG': ax, sign = 2, -1
                        elif t == 'G_AXIS_RY_POS': ax, sign = 3, +1
                        elif t == 'G_AXIS_RY_NEG': ax, sign = 3, -1
                        if ax is not None:
                            try:
                                val = float(js.get_axis(ax))
                            except Exception:
                                val = 0.0
                            if (sign > 0 and val >= thresh) or (sign < 0 and val <= -thresh):
                                return True
                        # Triggers
                        try:
                            if t == 'G_TRIG_LT' and float(js.get_axis(4)) >= 0.5:
                                return True
                            if t == 'G_TRIG_RT' and float(js.get_axis(5)) >= 0.5:
                                return True
                        except Exception:
                            pass
                except Exception:
                    return False
                return False

            def _kb_pressed(code: int | None) -> bool:
                if not isinstance(code, int):
                    return False
                keys = pygame.key.get_pressed()
                return bool(keys[code])

            held_mouse = pygame.mouse.get_pressed()[0]
            # Leer mapeos actuales desde InputConfig
            cfg = InputConfig()
            gp_token = cfg.get_gamepad_token_for_binding('gp_fireball')
            kb_a = cfg.get_key_for_binding('kb_fireball_a')
            kb_b = cfg.get_key_for_binding('kb_fireball_b')
            held_gp = _gp_pressed(gp_token)
            held_kb = _kb_pressed(kb_a) or _kb_pressed(kb_b)
            if self.fsm.context.get('automatic', False) and (held_mouse or held_gp or held_kb):
                # Godmode: omitir chequeo de maná para el jugador
                try:
                    world = entity.world
                    godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (entity.id == getattr(world, 'player_entity', None))
                except Exception:
                    godmode = False
                if not godmode:
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
                # Suficiente maná o godmode: proceder a preparar siguiente cast
                from roguelike_game.ecs.systems.fsm.states.spell.prepare_spell_state import PrepareSpellState
                self.fsm.change_state(PrepareSpellState(), entity)
                return
            # No recast: salir a estado global IdleState
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No requiere limpieza adicional
        pass