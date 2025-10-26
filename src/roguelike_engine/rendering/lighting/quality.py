from __future__ import annotations

import json
from pathlib import Path
from typing import Literal

_QUALITY = ("off", "ambient", "lights_low", "lights_high")
QualityTier = Literal["off", "ambient", "lights_low", "lights_high"]

_DEFAULT_PATH = Path("data/config/lighting.json")


def load_quality_config(path: Path | None = None) -> dict:
    p = path or _DEFAULT_PATH
    try:
        if p.exists():
            return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        pass
    return {}


def get_low_res_scale(cfg: dict) -> int:
    try:
        v = int(cfg.get("low_res_scale", 2))
        return max(1, min(8, v))
    except Exception:
        return 2


def get_max_lights(cfg: dict) -> int:
    try:
        v = int(cfg.get("max_lights_visible", 12))
        return max(0, min(256, v))
    except Exception:
        return 12


def get_max_radius(cfg: dict) -> int:
    try:
        v = int(cfg.get("max_radius", 192))
        return max(16, min(2048, v))
    except Exception:
        return 192


def get_quality_tier(cfg: dict) -> QualityTier:
    q = str(cfg.get("quality", "ambient") or "ambient").lower()
    return q if q in _QUALITY else "ambient"
