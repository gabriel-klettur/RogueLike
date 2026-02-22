"""Lightweight linter for Spawner_* set parameters."""
from __future__ import annotations
from typing import Any, Dict, List, Optional


def lint_set_params(set_id: str, params: Optional[Dict[str, Any]]) -> List[str]:
    """Return a list of warning strings for Spawner_* parameters.

    Rules are intentionally simple and non-fatal.
    """
    warnings: List[str] = []
    if not set_id:
        return ["empty set_id"]
    p = params or {}
    sid = str(set_id)
    try:
        def _is_int(v: Any) -> bool:
            return isinstance(v, int) and not isinstance(v, bool)

        def _gte0(v: Any) -> bool:
            return _is_int(v) and v >= 0

        # Common checks
        if 'max_active' in p and not _gte0(p['max_active']):
            warnings.append("max_active must be integer >= 0")
        if 'restart_cooldown_frames' in p and not _gte0(p['restart_cooldown_frames']):
            warnings.append("restart_cooldown_frames must be integer >= 0")
        if 'spawn_radius' in p:
            sr = p['spawn_radius']
            if isinstance(sr, (int, float)) and sr < 0:
                warnings.append("spawn_radius must be >= 0")
            elif isinstance(sr, str) and sr.lower() not in ("random", "aleatorio", "aleatoreo"):
                warnings.append("spawn_radius string must be 'random'/'aleatorio'/'aleatoreo'")

        # Per-set expectations
        if sid == 'Spawner_Periodic_Cooldown':
            if 'cooldown_frames' not in p:
                warnings.append("cooldown_frames missing for Periodic_Cooldown")
            elif not _gte0(p['cooldown_frames']):
                warnings.append("cooldown_frames must be integer >= 0")
        elif sid == 'Spawner_Periodic_BetweenWaves':
            if 'between_waves_cooldown_frames' not in p:
                warnings.append("between_waves_cooldown_frames missing for Periodic_BetweenWaves")
            elif not _gte0(p['between_waves_cooldown_frames']):
                warnings.append("between_waves_cooldown_frames must be integer >= 0")
        elif sid == 'Spawner_Waves_Clear':
            adv = p.get('advance_on')
            if adv and str(adv) != 'clear':
                warnings.append("advance_on should be 'clear' for Waves_Clear")

        # Shape validation
        if 'spawner_shape' in p:
            if str(p['spawner_shape']).lower() not in ('circle', 'square'):
                warnings.append("spawner_shape must be 'circle' or 'square'")
    except Exception as ex:
        warnings.append(f"linter error: {ex}")
    return warnings
