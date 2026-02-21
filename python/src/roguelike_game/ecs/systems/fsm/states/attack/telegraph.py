"""Telegraph rendering logic for melee wind-up phases."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional, Tuple

from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.combat.telegraph_arc import TelegraphArc


Vector2 = Tuple[float, float]
ColorRGBA = Tuple[int, int, int, int]


@dataclass(frozen=True)
class TelegraphConfig:
    """Parameters needed to render the arc telegraph."""

    radius: float
    arc_radians: float
    direction: Vector2
    color: ColorRGBA
    offset: float
    progress: float


def build_telegraph_config(spell_id: str, direction: Vector2, progress: float) -> TelegraphConfig:
    """Extract telegraph parameters from spell configuration data."""
    cfg = SPELLS.get(spell_id) or SPELLS.get("hostile_slash") or SPELLS.get("slash") or {}
    radius = _choose_value(cfg, primary="hit_radius", fallback="radius", default=40.0)
    arc_degrees = _choose_value(cfg, primary="hit_arc_degrees", fallback="arc_range_degrees", default=90.0)
    offset = float(cfg.get("offset", 0.0))
    color = _resolve_color(cfg)
    arc_radians = float(__import__("math").radians(arc_degrees))
    progress = max(0.0, min(1.0, progress))
    return TelegraphConfig(radius=radius, arc_radians=arc_radians, direction=direction, color=color, offset=offset, progress=progress)


def apply_telegraph(world: Any, entity_id: int, config: TelegraphConfig) -> None:
    """Write the telegraph component for the provided entity."""
    arc_map: Dict[int, TelegraphArc] = world.components.setdefault("TelegraphArc", {})
    arc_map[entity_id] = TelegraphArc(
        radius=config.radius,
        arc_angle=config.arc_radians,
        direction=config.direction,
        color=config.color,
        offset=config.offset,
        progress=config.progress,
    )


def clear_telegraph(world: Any, entity_id: int) -> None:
    """Remove TelegraphArc component from the entity if present."""
    world.components.get("TelegraphArc", {}).pop(entity_id, None)


def _choose_value(config: Dict[str, Any], primary: str, fallback: str, default: float) -> float:
    try:
        value = float(config.get(primary, 0.0))
        if value > 0:
            return value
        value = float(config.get(fallback, 0.0))
        if value > 0:
            return value
    except Exception:
        pass
    return default


def _resolve_color(config: Dict[str, Any]) -> ColorRGBA:
    color = _pick_color(config)
    alpha = max(0, min(255, int(config.get("telegraph_alpha", 90))))
    return int(color[0]), int(color[1]), int(color[2]), alpha


def _pick_color(config: Dict[str, Any]) -> Tuple[int, int, int]:
    for key in ("telegraph_color", "color"):
        candidate = config.get(key)
        if isinstance(candidate, (list, tuple)) and len(candidate) >= 3:
            return int(candidate[0]), int(candidate[1]), int(candidate[2])
    return 255, 230, 150
