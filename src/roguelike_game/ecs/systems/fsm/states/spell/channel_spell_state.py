from roguelike_game.ecs.systems.fsm.state import State
import time

from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.registry import SPELL_RESOLVERS

class ChannelSpellState(State):
    def enter(self, entity):
        self.fsm.context['channel_start'] = time.time()
        # Crear barra de hechizo
        spell = self.fsm.context.get('spell')
        cfg = SPELLS.get(spell, {})
        base = cfg.get('channel_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=self.fsm.context['channel_start'], active=True, state='channel')
        # cone_breath: iniciar efecto al comienzo del canalizado
        try:
            if cfg.get('type') == 'cone_breath':
                resolver = SPELL_RESOLVERS.get('cone_breath')
                if resolver is not None:
                    resolver.resolve(world, entity.id, self.fsm.context, cfg, self.fsm.context.get('camera'))
                # Guardar parámetros de crecimiento para actualizar en execute
                eff = getattr(cfg, 'extra', {}).get('effect', {}) if hasattr(cfg, 'extra') else cfg.get('effect', {})
                base_len = float(eff.get('length', cfg.get('length', 0.0)) or 0.0)
                max_mul = float(eff.get('max_length_multiplier', 1.75))
                max_time = float(eff.get('duration', cfg.get('channel_duration', 0.0)) or 0.0) or float(base)
                self.fsm.context['_cone_base_len'] = base_len
                self.fsm.context['_cone_max_len'] = max_len = max(base_len, base_len * max_mul)
                self.fsm.context['_cone_max_grow_time'] = max(0.0, max_time)
        except Exception:
            pass

    def execute(self, entity, dt):
        # Dinámica: duración/retención según hechizo
        spell = self.fsm.context.get('spell')
        cfg = SPELLS.get(spell, {})
        spell_type = cfg.get('type')
        # cone_breath: mantener mientras la tecla esté presionada y crecer longitud
        if spell_type == 'cone_breath':
            world = entity.world
            # Si jugador soltó la tecla o alcanzó su duración máxima opcional, finalizar canalización
            try:
                inp = world.components.get('InputComponent', {}).get(entity.id)
                holding = bool(getattr(inp, f'spell_{spell}', False)) if inp is not None else False
            except Exception:
                holding = False
            elapsed = time.time() - self.fsm.context.get('channel_start', time.time())
            max_time = float(self.fsm.context.get('_cone_max_grow_time', 0.0) or 0.0)
            if (not holding) or (max_time > 0.0 and elapsed >= max_time):
                from roguelike_game.ecs.systems.fsm.states.spell.release_spell_state import ReleaseSpellState
                self.fsm.change_state(ReleaseSpellState(), entity)
                return
            # Actualizar longitud dinámica del cono para todos los componentes del caster de este spell
            try:
                base_len = float(self.fsm.context.get('_cone_base_len', 0.0))
                max_len = float(self.fsm.context.get('_cone_max_len', base_len))
                if max_time > 0.0:
                    t = min(elapsed / max_time, 1.0)
                    new_len = base_len + (max_len - base_len) * t
                else:
                    # crece lentamente sin límite de tiempo
                    new_len = min(max_len, base_len + elapsed * (base_len * 0.25))
                comps = world.components.get('ConeBreathComponent', {})
                for _eid, comp in list(comps.items()):
                    if getattr(comp, 'owner', None) == entity.id and getattr(comp, 'spell_key', '') == spell:
                        try:
                            comp.length = float(new_len)
                            # Mantener vivo el componente mientras se canaliza
                            comp.start_time = time.time()
                            # Opcional: limitar duración nominal a una pequeña ventana para que no expire
                            if float(getattr(comp, 'duration', 0.0) or 0.0) < 0.5:
                                comp.duration = 0.5
                        except Exception:
                            pass
            except Exception:
                pass
            return
        # Default: duración de canalización temporal
        base = cfg.get('channel_duration', 0)
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
        # cone_breath: limpiar el componente activo del caster al finalizar canalización
        try:
            spell = self.fsm.context.get('spell')
            cfg = SPELLS.get(spell, {})
            if cfg.get('type') == 'cone_breath':
                cb_map = entity.world.components.get('ConeBreathComponent', {})
                for _eid in list(cb_map.keys()):
                    cbc = cb_map.get(_eid)
                    if getattr(cbc, 'owner', None) == entity.id and getattr(cbc, 'spell_key', '') == spell:
                        cb_map.pop(_eid, None)
                        entity.world.components.get('Position', {}).pop(_eid, None)
        except Exception:
            pass