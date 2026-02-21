"""Resolve particle preview dictionaries from raw definitions."""
from __future__ import annotations

from dataclasses import dataclass
import logging
from typing import Any, Dict, Tuple

from roguelike_game.config.particles_config import get_preset

logger = logging.getLogger(__name__)

Definition = Dict[str, Any]
ParticlesDict = Dict[str, Any]
MetaDict = Dict[str, Any]


@dataclass(frozen=True)
class ResolvedParticles:
    """Structured result returned by :func:`resolve_particles_dict_from_definition`."""

    particles: ParticlesDict
    meta: MetaDict


def resolve_particles_dict_from_definition(defn: Definition) -> Tuple[ParticlesDict, MetaDict]:
    """Return a normalized particles configuration and metadata for a definition."""

    resolved = _resolve(defn)
    return resolved.particles, resolved.meta


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _resolve(defn: Definition) -> ResolvedParticles:
    source_meta: MetaDict = {}
    base: ParticlesDict = {}
    overrides: ParticlesDict = {}

    vfx = defn.get("vfx")
    if isinstance(vfx, dict):
        source_meta["vfx_obj"] = vfx
        preset_id = vfx.get("preset") if isinstance(vfx.get("preset"), str) else None
        if preset_id:
            base = _load_preset_particles(preset_id, source_meta)
        pov = vfx.get("particles")
        if isinstance(pov, dict):
            overrides = dict(pov)
    elif isinstance(vfx, str):
        base = _load_preset_particles(vfx, source_meta)

    particles: ParticlesDict = {**base, **overrides}
    if "kind" not in particles:
        particles["kind"] = _infer_kind(defn, particles)

    return ResolvedParticles(particles=particles, meta=source_meta)


def _load_preset_particles(preset_id: str, meta: MetaDict) -> ParticlesDict:
    meta["sid"] = preset_id
    try:
        preset = get_preset(preset_id)
        if preset and isinstance(preset.vfx, dict):
            particles = preset.vfx.get("particles", {})
            if isinstance(particles, dict):
                return dict(particles)
    except Exception:
        logger.debug("Failed to load particle preset: %s", preset_id, exc_info=True)
    return {}


def _infer_kind(defn: Definition, particles: ParticlesDict) -> str:
    explicit = particles.get("kind")
    if isinstance(explicit, str) and explicit:
        return explicit

    stype = defn.get("type")
    if isinstance(stype, str):
        kind = _infer_kind_from_type(stype)
        if kind:
            return kind

    sid = str(defn.get("id") or "").lower()
    inferred = _infer_kind_from_identifier(sid)
    if inferred:
        return inferred

    return particles.get("kind", "smoke_emitter")


def _infer_kind_from_type(spell_type: str) -> str | None:
    mapping = {
        "aura": "aura",
        "sphere_magic_shield": "aura",
        "beam": "laser",
        "dash": "dash",
        "slash": "slash",
        "lightning": "lightning",
        "arcane_flame": "arcane_flame",
        "firework": "firework",
        "firework_launch": "firework",
        "smoke_emitter": "smoke_emitter",
        "smoke": "smoke",
        "teleport": "teleport",
    }
    return mapping.get(spell_type)


def _infer_kind_from_identifier(identifier: str) -> str | None:
    keywords = {
        "aura": "aura",
        "beam": "laser",
        "laser": "laser",
        "dash": "dash",
        "slash": "slash",
        "lightning": "lightning",
        "firework": "firework",
        "smoke_emitter": "smoke_emitter",
        "smoke": "smoke",
        "flame": "arcane_flame",
        "teleport": "teleport",
        "shield": "aura",
    }
    for keyword, kind in keywords.items():
        if keyword in identifier:
            return kind
    return None


__all__ = [
    "ResolvedParticles",
    "resolve_particles_dict_from_definition",
]
