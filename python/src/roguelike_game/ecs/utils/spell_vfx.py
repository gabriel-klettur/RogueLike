from __future__ import annotations

from typing import Any, Dict, Optional


def _get_vfx(cfg: Any) -> Dict[str, Any]:
    v = getattr(cfg, "vfx", None)
    if isinstance(v, dict):
        return v
    extra = getattr(cfg, "extra", {}) or {}
    v2 = extra.get("vfx")
    return v2 if isinstance(v2, dict) else {}


def _get_nested(d: Dict[str, Any], *keys: str) -> Optional[Any]:
    cur: Any = d
    for k in keys:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(k)
    return cur


def get_meteor_sprite_path(cfg: Any, default: str) -> str:
    vfx = _get_vfx(cfg)
    p = _get_nested(vfx, "meteor", "sprite", "path")
    return p if isinstance(p, str) and p else default


def get_meteor_scale(cfg: Any, default: float = 0.10) -> float:
    vfx = _get_vfx(cfg)
    val = _get_nested(vfx, "meteor", "sprite", "scale")
    if isinstance(val, (int, float)):
        return float(val)
    val = vfx.get("meteor_scale")
    if isinstance(val, (int, float)):
        return float(val)
    val = getattr(cfg, "scale", None)
    if isinstance(val, (int, float)):
        return float(val)
    return float(default)


def get_impact_sprite_path(cfg: Any, default: str) -> str:
    vfx = _get_vfx(cfg)
    p = _get_nested(vfx, "impact", "sprite", "path")
    return p if isinstance(p, str) and p else default


def get_impact_scale(cfg: Any, default: float = 0.10) -> float:
    vfx = _get_vfx(cfg)
    val = _get_nested(vfx, "impact", "sprite", "scale")
    if isinstance(val, (int, float)):
        return float(val)
    val = vfx.get("impact_scale")
    if isinstance(val, (int, float)):
        return float(val)
    val = _get_nested(vfx, "sprite", "scale")
    if isinstance(val, (int, float)):
        return float(val)
    val = getattr(cfg, "scale", None)
    if isinstance(val, (int, float)):
        return float(val)
    return float(default)
