"""VFX spawning utilities shared by fireball collision handlers."""
from __future__ import annotations

from typing import Any, Dict, Iterable, Optional, Sequence, Tuple

import pygame

from roguelike_game.ecs.components.abilities.explosion_component import (
    ExplosionComponent,
)
from roguelike_game.ecs.components.particles.particle_preset_component import (
    ParticlePresetComponent,
)
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.systems.combat.explosions_models import FireExplosionModel, TimedEffectModel


def spawn_impact_effects(
    world: Any,
    spell_cfg: Dict[str, Any],
    position: Tuple[float, float],
    scale_multiplier: float,
) -> None:
    """Spawn particle and explosion feedback for an impact."""

    vfx = _extract_vfx(spell_cfg)
    impact_cfg = vfx.get("impact", {}) if isinstance(vfx, dict) else {}

    preset_id, ttl = _extract_preset(impact_cfg)
    if preset_id:
        _spawn_preset(world, position, preset_id, ttl, scale_multiplier)
        return

    explosion_cfg = impact_cfg.get("explosion") if isinstance(impact_cfg, dict) else None
    if isinstance(explosion_cfg, dict):
        _spawn_explosion(world, position, explosion_cfg, scale_multiplier)


def _extract_vfx(cfg: Dict[str, Any]) -> Dict[str, Any]:
    if isinstance(cfg, dict):
        vfx = cfg.get("vfx")
        if isinstance(vfx, dict):
            return vfx
        extra = cfg.get("extra")
        if isinstance(extra, dict):
            nested = extra.get("vfx")
            if isinstance(nested, dict):
                return nested
    vfx_attr = getattr(cfg, "vfx", None)
    if isinstance(vfx_attr, dict):
        return vfx_attr
    extra_attr = getattr(cfg, "extra", None)
    if isinstance(extra_attr, dict):
        nested = extra_attr.get("vfx")
        if isinstance(nested, dict):
            return nested
    return {}


def _extract_preset(impact_cfg: Dict[str, Any]) -> Tuple[Optional[str], Optional[int]]:
    preset = impact_cfg.get("preset") if isinstance(impact_cfg, dict) else None
    ttl_raw = impact_cfg.get("ttl") if isinstance(impact_cfg, dict) else None
    explosion_cfg = impact_cfg.get("explosion") if isinstance(impact_cfg, dict) else None

    if not preset and isinstance(explosion_cfg, dict):
        preset = explosion_cfg.get("preset")
        ttl_raw = explosion_cfg.get("ttl")

    ttl = int(ttl_raw) if isinstance(ttl_raw, (int, float)) else None
    return preset if isinstance(preset, str) else None, ttl


def _spawn_preset(
    world: Any,
    position: Tuple[float, float],
    preset_id: str,
    ttl: Optional[int],
    scale_multiplier: float,
) -> None:
    entity = world.create_entity()
    world.components.setdefault("Position", {})[entity] = Position(*position)
    world.components.setdefault("ParticlePresetComponent", {})[entity] = ParticlePresetComponent(
        preset_id,
        scale_multiplier=scale_multiplier,
    )
    ttl_ticks = ttl if ttl is not None else 30
    world.components.setdefault("ExplosionComponent", {})[entity] = ExplosionComponent(
        TimedEffectModel(ttl_ticks)
    )


def _spawn_explosion(
    world: Any,
    position: Tuple[float, float],
    cfg: Dict[str, Any],
    scale_multiplier: float,
) -> None:
    particle_count = int(cfg.get("particle_count")) if isinstance(cfg.get("particle_count"), int) else 100
    scale = float(cfg.get("scale")) if isinstance(cfg.get("scale"), (int, float)) else 1.0
    scale *= scale_multiplier

    colors = _extract_colors(cfg.get("colors"))
    gravity = _extract_vector(cfg.get("gravity"))
    drag = float(cfg.get("drag")) if isinstance(cfg.get("drag"), (int, float)) else None
    blend_mode = cfg.get("blend_mode") if isinstance(cfg.get("blend_mode"), str) else None
    size_over_life = _ensure_sequence(cfg.get("size_over_life"))
    alpha_over_life = _ensure_sequence(cfg.get("alpha_over_life"))
    color_over_life = _ensure_sequence(cfg.get("color_over_life"))

    entity = world.create_entity()
    world.components.setdefault("Position", {})[entity] = Position(*position)
    world.components.setdefault("ExplosionComponent", {})[entity] = ExplosionComponent(
        FireExplosionModel(
            position[0],
            position[1],
            particle_count=particle_count,
            scale=scale,
            colors=colors,
            gravity=gravity,
            drag=drag,
            blend_mode=blend_mode,
            size_over_life=size_over_life,
            alpha_over_life=alpha_over_life,
            color_over_life=color_over_life,
        )
    )


def _extract_colors(colors: Optional[Iterable[Iterable[float]]]) -> Optional[list[Tuple[int, int, int]]]:
    if not isinstance(colors, (list, tuple)):
        return None
    palette: list[Tuple[int, int, int]] = []
    for color in colors:
        if isinstance(color, (list, tuple)) and len(color) >= 3:
            palette.append((int(color[0]), int(color[1]), int(color[2])))
    return palette or None


def _extract_vector(vec: Optional[Sequence[float]]) -> Optional[Tuple[float, float]]:
    if isinstance(vec, (int, float)):
        return 0.0, float(vec)
    if isinstance(vec, (list, tuple)) and len(vec) >= 2:
        return float(vec[0]), float(vec[1])
    return None


def _ensure_sequence(value: Optional[Sequence[float]]) -> Optional[Sequence[float]]:
    return value if isinstance(value, (list, tuple)) else None
