"""Main entry point for building particle preview objects."""
from __future__ import annotations

import logging
from typing import Any, Callable, Dict

from .context import ParticlePreviewContext, build_context
from .factories import aura, directional, environmental, projectile, smoke
from .resolver import resolve_particles_dict_from_definition

logger = logging.getLogger(__name__)

Factory = Callable[[ParticlePreviewContext], Any]


_ALIAS_MAP: Dict[str, str] = {
    "firework_launch": "firework",
    "fountain": "water_fountain",
    "leaf": "falling_leaf",
    "water": "water_flow",
}

_FACTORY_MAP: Dict[str, Factory] = {
    "smoke_emitter": smoke.build_smoke_emitter,
    "smoke": smoke.build_smoke_burst,
    "firework": projectile.build_firework,
    "lightning": directional.build_lightning,
    "aura": aura.build_aura,
    "dash": directional.build_dash,
    "slash": directional.build_slash,
    "laser": directional.build_laser,
    "arcane_flame": projectile.build_arcane_flame,
    "teleport": environmental.build_teleport,
    "water_fountain": environmental.build_water_fountain,
    "falling_leaf": environmental.build_falling_leaf,
    "explosion": projectile.build_explosion,
    "water_flow": environmental.build_water_flow,
}


def build_preview_for_definition(defn: Dict[str, Any]):
    """Construct the appropriate preview object for a spell/effect definition."""

    try:
        parts, meta = resolve_particles_dict_from_definition(defn)
        context = build_context(defn, parts, meta)
        kind = _normalize_kind(parts.get("kind"))
        factory = _select_factory(kind)
        if factory is None:
            return smoke.build_default_smoke(context)
        return factory(context)
    except Exception:  # pragma: no cover - defensive logging around preview builds
        logger.exception("[preview_builder] Failed to build preview for definition: %s", defn)
        return None


def _normalize_kind(raw_kind: Any) -> str:
    if isinstance(raw_kind, str) and raw_kind.strip():
        return raw_kind.strip().lower()
    return "smoke_emitter"


def _select_factory(kind: str) -> Factory | None:
    normalized = _ALIAS_MAP.get(kind, kind)
    return _FACTORY_MAP.get(normalized)


__all__ = ["build_preview_for_definition"]
