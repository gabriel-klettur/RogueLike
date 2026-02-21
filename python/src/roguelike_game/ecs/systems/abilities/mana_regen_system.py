import time
from typing import Dict


class ManaRegenSystem:
    """
    Regenera maná por segundo según configuración por clase.

    - Para entidades con componente Mana, incrementa current_mana a razón de
      regen_per_second * dt, con tope en max_mana.
    - Para jugadores, obtiene el rate desde PLAYER_STATS[class]['mana_regen_per_second']
      si existe; si no, usa 1.0 por defecto. Para otras entidades, usa 0.5 por defecto.
    - No regenera si la entidad tiene DeathTimer activo.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._last_time: float = 0.0
        # Acumulador por entidad para fracciones de regeneración
        self._accum: Dict[int, float] = {}

    def update(self, world, *args):
        now = time.time()
        if self._last_time == 0.0:
            self._last_time = now
            return
        dt = max(0.0, now - self._last_time)
        self._last_time = now

        comps = world.components
        mana_dict: Dict[int, object] = comps.get('Mana', {})
        if not mana_dict:
            return
        death = comps.get('DeathTimer', {})
        players = comps.get('PlayerTagComponent', {})
        npc_states = comps.get('NPCState', {})

        # Lazy import to avoid circular deps at module import
        try:
            from roguelike_game.factories.player.config import PLAYER_STATS
        except Exception:
            PLAYER_STATS = {}

        for eid, mana in list(mana_dict.items()):
            if eid in death:
                # Limpiar acumulador si está muerto
                if eid in self._accum:
                    self._accum.pop(eid, None)
                continue
            max_mana = getattr(mana, 'max_mana', 0)
            cur_mana = getattr(mana, 'current_mana', 0)
            if max_mana is None:
                continue
            try:
                if eid in players:
                    # PlayerTagComponent stores class in 'class_name'
                    ptag = players.get(eid)
                    cls = getattr(ptag, 'class_name', None)
                    regen = None
                    if cls and isinstance(PLAYER_STATS, dict):
                        try:
                            regen = float(PLAYER_STATS.get(cls, {}).get('mana_regen_per_second', 1.0))
                        except Exception:
                            regen = 1.0
                    if regen is None:
                        regen = 1.0
                    # Gate regen to Idle-like only for players
                    try:
                        npc_state = npc_states.get(eid)
                        if npc_state is None:
                            # If no FSM, fallback to velocity==0
                            vel = world.components.get('Velocity', {}).get(eid)
                            if not vel or (getattr(vel, 'vx', 0) == 0 and getattr(vel, 'vy', 0) == 0):
                                pass  # allow regen
                            else:
                                continue
                        else:
                            fsm = getattr(npc_state, 'fsm', None)
                            cur_state_name = fsm.current_state.__class__.__name__ if fsm and getattr(fsm, 'current_state', None) else None
                            # Permitido si es Idle/Cooldown explícito
                            if cur_state_name and (('Idle' in cur_state_name) or ('Cooldown' in cur_state_name)):
                                pass
                            else:
                                # Si no es Idle/Cooldown, permitir si está quieto y no está casteando/atacando
                                vel = world.components.get('Velocity', {}).get(eid)
                                still = (not vel) or (getattr(vel, 'vx', 0) == 0 and getattr(vel, 'vy', 0) == 0)
                                disallow_states = ('Prepare', 'Channel', 'Cast', 'Attack')
                                in_disallowed = False
                                if cur_state_name:
                                    in_disallowed = any(p in cur_state_name for p in disallow_states)
                                if not (still and not in_disallowed):
                                    continue
                    except Exception:
                        # On errors resolving state, skip regen for safety
                        continue
                else:
                    regen = 0.5
            except Exception:
                regen = 1.0
            if regen <= 0 or dt <= 0:
                continue
            # Si ya está lleno, no acumular
            if float(cur_mana) >= float(max_mana):
                if eid in self._accum:
                    self._accum.pop(eid, None)
                continue
            # Acumular fracción y aplicar solo los enteros
            acc = float(self._accum.get(eid, 0.0)) + float(regen) * dt
            add_int = int(acc)
            acc = acc - add_int
            if add_int > 0:
                new_val = float(cur_mana) + add_int
                if new_val > float(max_mana):
                    new_val = float(max_mana)
                    acc = 0.0
                try:
                    setattr(mana, 'current_mana', int(new_val))
                except Exception:
                    try:
                        mana.current_mana = int(new_val)
                    except Exception:
                        pass
            # Guardar resto acumulado
            if float(cur_mana) < float(max_mana):
                self._accum[eid] = acc
            else:
                self._accum.pop(eid, None)
