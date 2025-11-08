from __future__ import annotations

from typing import Any, Dict, List
import json
import logging

from roguelike_engine.config.config import LIGHT_INSTANCES_PATH, LIGHT_PRESETS_PATH
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from .light_types import Light

_log = logging.getLogger(__name__)

_LOADED: bool = False


def _read_json(path: str, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return default
    except Exception:
        return default


def _load_presets() -> Dict[str, Dict[str, Any]]:
    raw = _read_json(LIGHT_PRESETS_PATH, {})
    p = raw.get("presets") if isinstance(raw, dict) else None
    return p if isinstance(p, dict) else {}


def _merge_preset(preset: Dict[str, Any], overrides: Dict[str, Any] | None) -> Dict[str, Any]:
    base = dict(preset or {})
    if isinstance(overrides, dict):
        for k, v in overrides.items():
            base[k] = v
    return base


def load_persistent_to_manager(lm) -> int:
    global _LOADED
    if _LOADED:
        return 0
    instances: List[Dict[str, Any]] = _read_json(LIGHT_INSTANCES_PATH, [])
    presets = _load_presets()
    added = 0
    for idx, e in enumerate(instances or []):
        try:
            preset_id = str(e.get("preset_id") or "")
            zone = str(e.get("zone") or "no zone")
            rel_x = int(e.get("rel_x") or 0)
            rel_y = int(e.get("rel_y") or 0)
            ov = e.get("overrides") if isinstance(e, dict) else None
            preset = presets.get(preset_id, {}) if isinstance(presets, dict) else {}
            params = _merge_preset(preset, ov if isinstance(ov, dict) else None)
            # Defaults for robustness
            radius = int(params.get("radius", 160))
            color = tuple(params.get("color", (255, 200, 140)))
            intensity = float(params.get("intensity", 1.0))
            falloff = float(params.get("falloff", 2.0))
            flicker_amp = float(params.get("flicker_amp", 0.0))
            flicker_speed = float(params.get("flicker_speed", 2.3))
            center_scale = float(params.get("center_scale", 1.0))
            # Compute world coords from zone offsets
            off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
            origin_px_x = int(off_tx) * TILE_SIZE
            origin_px_y = int(off_ty) * TILE_SIZE
            wx = float(origin_px_x + rel_x)
            wy = float(origin_px_y + rel_y)
            lid = f"persist:{e.get('id', idx+1)}"
            lm.add(
                Light(
                    x=wx,
                    y=wy,
                    radius=radius,
                    color=color,
                    intensity=intensity,
                    falloff=falloff,
                    enabled=False,
                    flicker_amp=flicker_amp,
                    flicker_speed=flicker_speed,
                    center_scale=center_scale,
                    id=lid,
                )
            )
            added += 1
        except Exception:
            continue
    _LOADED = True
    _log.info(f"[Lighting] Loaded {added} persistent light instance(s) from {LIGHT_INSTANCES_PATH}")
    return added
