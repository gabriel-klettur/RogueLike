from __future__ import annotations

from typing import Any, Dict, Tuple
import sys


def _get_tile_size() -> int:
    """Resolve TILE_SIZE, preferring lighting_controller override for tests."""
    try:
        ctrl_mod = sys.modules.get("roguelike_editors.lighting.lighting_controller")
        if ctrl_mod is not None and hasattr(ctrl_mod, "TILE_SIZE"):
            return int(getattr(ctrl_mod, "TILE_SIZE"))
    except Exception:
        pass
    try:
        from roguelike_engine.config.config_tiles import TILE_SIZE as _TS  # type: ignore

        return int(_TS)
    except Exception:
        return 32


def _get_global_map_settings():
    """Resolve global_map_settings, preferring lighting_controller override for tests."""
    try:
        ctrl_mod = sys.modules.get("roguelike_editors.lighting.lighting_controller")
        if ctrl_mod is not None and hasattr(ctrl_mod, "global_map_settings"):
            return getattr(ctrl_mod, "global_map_settings")
    except Exception:
        pass
    try:
        from roguelike_engine.config.map_config import global_map_settings as _GMS  # type: ignore

        return _GMS
    except Exception:
        class _Fallback:
            zone_offsets = {}

        return _Fallback()


def get_cam_params(cam: Any | None) -> Tuple[float, float, float]:
    """Return (z, ox, oy) camera parameters, safe defaults."""
    if cam is None:
        return 1.0, 0.0, 0.0
    z = float(getattr(cam, "zoom", 1.0) or 1.0)
    ox = float(getattr(cam, "offset_x", 0.0) or 0.0)
    oy = float(getattr(cam, "offset_y", 0.0) or 0.0)
    return z, ox, oy


def screen_to_world(mx: int, my: int, cam: Any | None) -> Tuple[float, float]:
    """Convert screen coords to world using camera (with subpixel rounding like original)."""
    if cam is None:
        return float(mx), float(my)
    z = float(getattr(cam, "zoom", 1.0) or 1.0)
    ox = round(getattr(cam, "offset_x", 0.0) * z) / z
    oy = round(getattr(cam, "offset_y", 0.0) * z) / z
    wx = (mx / z) + ox
    wy = (my / z) + oy
    return float(wx), float(wy)


def compute_instance_world(zone: str, rel_x: int, rel_y: int) -> Tuple[int, int]:
    """Compute world tile-based coordinates from instance data and global map offsets."""
    gms = _get_global_map_settings()
    ts = _get_tile_size()
    off_tx, off_ty = getattr(gms, "zone_offsets", {}).get(zone, (0, 0))
    wx = int(off_tx) * ts + rel_x
    wy = int(off_ty) * ts + rel_y
    return wx, wy


def world_to_screen(wx: float, wy: float, z: float, ox: float, oy: float) -> Tuple[int, int]:
    """Project world coords to screen coords using camera params."""
    sx = int((wx - ox) * z)
    sy = int((wy - oy) * z)
    return sx, sy


def merge_params(presets: Dict[str, Dict[str, Any]] | None, preset_id: str, overrides: Dict[str, Any] | None) -> Dict[str, Any]:
    """Return merged parameters from preset base + overrides."""
    base: Dict[str, Any] = presets.get(preset_id, {}) if isinstance(presets, dict) else {}
    params: Dict[str, Any] = dict(base)
    if isinstance(overrides, dict):
        for k, v in overrides.items():
            params[k] = v
    return params


def get_presets() -> Dict[str, Dict[str, Any]]:
    """Fetch presets via lighting_controller module if available (test monkeypatch), else service fallback."""
    try:
        ctrl_mod = sys.modules.get("roguelike_editors.lighting.lighting_controller")
        if ctrl_mod is not None and hasattr(ctrl_mod, "_load_presets"):
            return dict(getattr(ctrl_mod, "_load_presets")())  # type: ignore
    except Exception:
        pass
    try:
        from roguelike_editors.lighting.services.light_instances_service import _load_presets as _svc_lp  # type: ignore

        return dict(_svc_lp())
    except Exception:
        return {}


def get_light_instances() -> list[dict]:
    """Fetch light instances via lighting_controller module if available (test monkeypatch), else service fallback."""
    try:
        ctrl_mod = sys.modules.get("roguelike_editors.lighting.lighting_controller")
        if ctrl_mod is not None and hasattr(ctrl_mod, "load_light_instances"):
            res = getattr(ctrl_mod, "load_light_instances")()  # type: ignore
            return list(res) if isinstance(res, (list, tuple)) else []
    except Exception:
        pass
    try:
        from roguelike_editors.lighting.services.light_instances_service import load_light_instances as _svc_li  # type: ignore

        res = _svc_li()
        return list(res) if isinstance(res, (list, tuple)) else []
    except Exception:
        return []
