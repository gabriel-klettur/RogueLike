from __future__ import annotations

import time
from typing import Any, Dict
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker

class SpawnerDamageSystem:
    """
    Consumes SpawnerDamageEvents, manages spawner HP pools (per-state or shared),
    triggers building flash on hit, and performs state transitions via next_step_by_hp.

    Stores health in world.components['SpawnerHealth'] as:
      { eid: {
          'scope': 'per_state' | 'shared',
          'last_token': str | None,
          'shared': { 'current_hp': float, 'max_hp': int } | None,
          'by_state': { token: { 'current_hp': float, 'max_hp': int } }
        }
      }
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    # --- Helpers ---
    @staticmethod
    def _cur_token(st) -> str:
        try:
            tok = getattr(st, 'visual_override_token', None)
            if tok:
                return str(tok).strip().lower()
        except Exception:
            pass
        try:
            cur = getattr(st, 'fsm_state', None)
            return str(cur).strip().lower() if cur is not None else ''
        except Exception:
            return ''

    @staticmethod
    def _merge_life(cfg, token: str) -> Dict[str, Any]:
        eff: Dict[str, Any] = {}
        try:
            base = getattr(cfg, 'life_defaults', None) or {}
            if isinstance(base, dict):
                eff.update(base)
            life_map = getattr(cfg, 'visuals_life', None) or {}
            if token and isinstance(life_map, dict) and token in life_map and isinstance(life_map[token], dict):
                eff.update(life_map[token])
        except Exception:
            pass
        return eff

    @staticmethod
    def _init_pool(entry: Dict[str, Any], scope: str, token: str, eff: Dict[str, Any], prev_ratio: float | None, reset: str) -> None:
        max_hp = int(eff.get('max_hp', 0) or 0)
        if max_hp <= 0:
            max_hp = 1
        if scope == 'shared':
            cur = entry.get('shared')
            if cur is None:
                entry['shared'] = {'current_hp': float(max_hp), 'max_hp': int(max_hp)}
            else:
                # Shared scope ignores reset-on-enter by default: preserve current and max unless never set
                if cur.get('max_hp') is None:
                    cur['max_hp'] = int(max_hp)
                    if cur.get('current_hp') is None:
                        cur['current_hp'] = float(max_hp)
        else:
            pools = entry.setdefault('by_state', {})
            cur = pools.get(token)
            if cur is None:
                # Apply reset policy
                if reset == 'keep_value' and prev_ratio is not None:
                    # No absolute value available; treat as keep_ratio
                    reset = 'keep_ratio'
                if reset == 'keep_ratio' and prev_ratio is not None:
                    pools[token] = {
                        'max_hp': int(max_hp),
                        'current_hp': float(max_hp * max(0.0, min(prev_ratio, 1.0)))
                    }
                elif reset == 'no_change' and prev_ratio is not None:
                    pools[token] = {
                        'max_hp': int(max_hp),
                        'current_hp': float(max_hp * max(0.0, min(prev_ratio, 1.0)))
                    }
                else:
                    # set_to_max
                    pools[token] = {'max_hp': int(max_hp), 'current_hp': float(max_hp)}
            else:
                # Pool exists: keep as-is, but if max differs, keep current ratio
                try:
                    old_max = float(cur.get('max_hp', max_hp) or max_hp)
                    ratio = (float(cur.get('current_hp', old_max)) / old_max) if old_max > 0 else 1.0
                except Exception:
                    ratio = 1.0
                cur['max_hp'] = int(max_hp)
                cur['current_hp'] = float(max_hp * max(0.0, min(ratio, 1.0)))

    @staticmethod
    def _get_pool(entry: Dict[str, Any], scope: str, token: str) -> Dict[str, Any] | None:
        if scope == 'shared':
            return entry.get('shared')
        return entry.get('by_state', {}).get(token)

    @staticmethod
    def _active_building_for(world, eid: int):
        for b in getattr(world, 'buildings', []) or []:
            try:
                if getattr(b, '_spawner_eid', None) == eid and not getattr(b, 'runtime_hidden', False):
                    return b
            except Exception:
                continue
        return None

    def update(self, world, camera=None):
        comps = world.components
        cfg_map = comps.get('SpawnerConfig', {})
        st_map = comps.get('SpawnerState', {})
        if not cfg_map or not st_map:
            # No spawners
            try:
                sev = comps.get('SpawnerDamageEvents')
                if isinstance(sev, list):
                    sev.clear()
            except Exception:
                pass
            return

        # Initialize and handle token transitions (enter policies)
        health_map: Dict[int, Dict[str, Any]] = comps.setdefault('SpawnerHealth', {})
        for eid in list(cfg_map.keys()):
            cfg = cfg_map.get(eid)
            st = st_map.get(eid)
            if cfg is None or st is None:
                continue
            token = self._cur_token(st)
            if not token:
                continue
            scope = str(getattr(cfg, 'hp_scope', 'per_state') or 'per_state').lower()
            entry = health_map.setdefault(eid, {'scope': scope, 'last_token': None, 'shared': None, 'by_state': {}})
            # Detect token change
            last_tok = entry.get('last_token')
            if last_tok != token:
                # Compute previous ratio if available (per_state only)
                prev_ratio = None
                if scope != 'shared' and isinstance(entry.get('by_state'), dict):
                    prev = entry['by_state'].get(last_tok) if last_tok else None
                    if prev:
                        try:
                            prev_ratio = (float(prev.get('current_hp', 0)) / float(prev.get('max_hp', 1))) if float(prev.get('max_hp', 1)) > 0 else 1.0
                        except Exception:
                            prev_ratio = None
                eff = self._merge_life(cfg, token)
                # Use a safe fallback when cfg.life_defaults is None
                base_ld = getattr(cfg, 'life_defaults', None) or {}
                reset = str(eff.get('hp_reset_on_enter', base_ld.get('hp_reset_on_enter', 'set_to_max'))).strip().lower()
                self._init_pool(entry, scope, token, eff, prev_ratio, reset)
                entry['last_token'] = token

        # Consume damage events
        events = comps.get('SpawnerDamageEvents', []) or []
        if events:
            for evt in list(events):
                try:
                    eid = int(evt.get('spawner_eid'))
                    dmg = float(evt.get('damage', 0))
                except Exception:
                    continue
                cfg = cfg_map.get(eid)
                st = st_map.get(eid)
                if cfg is None or st is None:
                    continue
                # Determine if the attacker is the player (to show centered HUD like with monsters)
                attacker = evt.get('attacker')
                is_player_attacker = False
                try:
                    if attacker is not None and attacker in world.components.get('PlayerTagComponent', {}):
                        is_player_attacker = True
                except Exception:
                    is_player_attacker = False
                # Godmode attacker: if the attacker is the player and godmode is active, one-shot the spawner
                gm_attacker = False
                try:
                    gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player_attacker
                except Exception:
                    gm_attacker = False
                token = self._cur_token(st)
                eff = self._merge_life(cfg, token)
                if not bool(eff.get('damageable', False)):
                    continue
                entry = health_map.setdefault(eid, {'scope': getattr(cfg, 'hp_scope', 'per_state'), 'last_token': token, 'shared': None, 'by_state': {}})
                scope = str(entry.get('scope', getattr(cfg, 'hp_scope', 'per_state'))).lower()
                pool = self._get_pool(entry, scope, token)
                # Initialize if missing
                if pool is None:
                    # Use a safe fallback when cfg.life_defaults is None
                    base_ld = getattr(cfg, 'life_defaults', None) or {}
                    reset = str(eff.get('hp_reset_on_enter', base_ld.get('hp_reset_on_enter', 'set_to_max'))).strip().lower()
                    self._init_pool(entry, scope, token, eff, prev_ratio=None, reset=reset)
                    pool = self._get_pool(entry, scope, token)
                if not pool:
                    continue
                # Apply damage
                try:
                    if gm_attacker:
                        pool['current_hp'] = 0.0
                    else:
                        pool['current_hp'] = max(0.0, float(pool.get('current_hp', 0)) - dmg)
                except Exception:
                    pool['current_hp'] = 0.0
                # Record last attacker for KO attribution and debugging
                try:
                    if attacker is not None:
                        world.components.setdefault('LastAttacker', {})[eid] = LastAttacker(int(attacker), float(time.time()))
                except Exception:
                    pass
                # Publish/refresh Health component so TargetHudRenderSystem can render the bar
                try:
                    world.components.setdefault('Health', {})[eid] = Health(
                        current_hp=int(max(0, int(pool.get('current_hp', 0)))) if isinstance(pool.get('current_hp'), (int, float)) else int(eff.get('max_hp', 0) or 0),
                        max_hp=int(eff.get('max_hp', pool.get('max_hp', 1)))
                    )
                except Exception:
                    pass
                # Make this spawner the active HUD target (centered) only if attacker is the player
                if is_player_attacker:
                    try:
                        hud = world.components.setdefault('TargetHUD', {})
                        hud['target_eid'] = int(eid)
                        hud['last_hit_time'] = float(time.time())
                        hud.setdefault('ttl_s', 3.0)
                    except Exception:
                        pass
                    # Publish combo event for hit attributed to the player
                    try:
                        combo_q = world.components.setdefault('ComboEventQueue', [])
                        combo_q.append({
                            'attacker': int(attacker),
                            'target': int(eid),
                            'damage': float(dmg),
                            'source': 'hitbox',
                            'time': float(time.time()),
                        })
                    except Exception:
                        pass
                # Trigger flash
                try:
                    if bool(eff.get('flash_on_hit', True)):
                        color = tuple(eff.get('flash_color', [255, 255, 255]))
                        duration = float(eff.get('flash_duration_s', 0.08) or 0.08)
                        b = self._active_building_for(world, eid)
                        if b and hasattr(b, 'trigger_flash'):
                            b.trigger_flash(color=color, duration=duration)
                except Exception:
                    pass
                # Check HP <= 0
                try:
                    if float(pool.get('current_hp', 0)) <= 0.0:
                        next_state = eff.get('next_step_by_hp')
                        if isinstance(next_state, str) and next_state:
                            try:
                                st.visual_override_token = next_state
                                entry['last_token'] = str(next_state).strip().lower()
                            except Exception:
                                pass
                        if bool(eff.get('end_logic', False)):
                            try:
                                st.finished = True
                                st.fsm_state = 'finished'
                            except Exception:
                                pass
                        # Combo kill event when player kills a spawner
                        if is_player_attacker:
                            try:
                                combo_q = world.components.setdefault('ComboEventQueue', [])
                                combo_q.append({'type': 'kill', 'entity': int(attacker), 'target': int(eid)})
                            except Exception:
                                pass
                except Exception:
                    pass
        # Clear processed events
        try:
            if isinstance(events, list):
                events.clear()
        except Exception:
            comps['SpawnerDamageEvents'] = []
