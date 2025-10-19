from __future__ import annotations

from typing import Any, Dict, Optional, Tuple
import logging

from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig

logger = logging.getLogger(__name__)

# Optional: FSM Editor bridge (validation of set ids). Keep non-fatal.
try:  # pragma: no cover
    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set as _fsm_get_set
except Exception:  # pragma: no cover
    _fsm_get_set = None


def compile_fsm_set(cfg: SpawnerConfig) -> Tuple[str, Dict[str, Any]]:
    """Derive an editor-friendly FSM set id and params from the resolved config.

    Purely metadata for tools/UI.
    """
    try:
        pol = dict(getattr(cfg, 'policy', {}) or {})
        trig = str(((getattr(cfg, 'trigger', {}) or {}).get('type') or 'proximity')).lower()
        advance_on = str(pol.get('advance_on', 'cooldown') or 'cooldown').lower()
        bwc_frames = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
        bwc = bwc_frames > 0
        prox_init_only = bool(pol.get('proximity_initial_only', False))
        loop = bool(pol.get('restart_on_done') or pol.get('loop') or pol.get('repeat'))
        max_active = int(pol.get('max_active', 0) or 0)
        mode = pol.get('mode', '')
        if advance_on == 'clear':
            set_id = 'Spawner_Waves_Clear'
        elif bwc:
            set_id = 'Spawner_Periodic_BetweenWaves'
        else:
            set_id = 'Spawner_Periodic_Cooldown'
        params: Dict[str, Any] = {
            'trigger': trig,
            'advance_on': advance_on,
            'between_waves_cooldown_frames': bwc_frames,
            'proximity_initial_only': prox_init_only,
            'loop': loop,
            'cooldown_frames': int(getattr(cfg, 'cooldown_frames', 0) or 0),
            'restart_cooldown_frames': int(getattr(cfg, 'restart_cooldown_frames', 0) or 0),
            'max_active': max_active,
            'mode': mode,
            'spawner_shape': getattr(cfg, 'spawner_shape', 'circle'),
            'spawn_radius': getattr(cfg, 'spawn_radius', None),
            'template_id': getattr(cfg, 'template_id', ''),
        }
        return set_id, params
    except Exception:
        return 'Spawner_Periodic_Cooldown', {'error': 'compile_failed'}


def fsm_override_from(tpl: Dict[str, Any], inst: Dict[str, Any]) -> Tuple[Optional[str], Dict[str, Any]]:
    """Read optional FSM override from template/instance/overrides (dot-notation)."""
    set_id: Optional[str] = None
    params: Dict[str, Any] = {}
    try:
        tfsm = tpl.get('fsm') if isinstance(tpl, dict) else None
        if isinstance(tfsm, dict):
            if isinstance(tfsm.get('params'), dict):
                params.update(tfsm['params'])
            if isinstance(tfsm.get('set_id'), str):
                set_id = tfsm['set_id']
        if isinstance(inst, dict) and isinstance(inst.get('fsm'), dict):
            if isinstance(inst['fsm'].get('params'), dict):
                params.update(inst['fsm']['params'])
            if isinstance(inst['fsm'].get('set_id'), str):
                set_id = inst['fsm']['set_id'] or set_id
        ov = inst.get('overrides', {}) if isinstance(inst, dict) else {}
        if isinstance(ov, dict):
            for k, v in ov.items():
                if k == 'fsm.set_id' and isinstance(v, str):
                    set_id = v
                elif k.startswith('fsm.params.'):
                    key = k.split('.', 2)[2] if '.' in k else None
                    if key:
                        params[key] = v
    except Exception:
        pass
    return set_id, params


def validate_set_id(set_id: str) -> Optional[bool]:
    """Validate a set id against the runtime registry if available.

    Returns True if known, False if known invalid, or None if no registry.
    """
    if _fsm_get_set is None:
        return None
    try:
        return _fsm_get_set(set_id) is not None
    except Exception:
        return None
