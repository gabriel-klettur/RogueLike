import logging
from typing import Any, Dict, List, Tuple, Optional

# Type-only import to avoid heavy coupling
from roguelike_game.config.spells_config import SpellConfig

logger = logging.getLogger(__name__)


def apply_aura_cfg(aura: Any, cfg: SpellConfig) -> None:
    """Apply flat SpellConfig parameters to an existing Aura-like component.

    Copies generic area/buff fields and VFX particle parameters. Performs
    safe type conversions and respects overrides inside buff.
    """
    # Core area fields
    v = cfg.get("radius", None)
    if v is not None:
        aura.radius = v
    v = cfg.get("duration", None)
    if v is not None:
        aura.duration = v

    # Buff dictionary (e.g., healing params)
    new_buff = cfg.get("buff", None)
    if isinstance(new_buff, dict):
        aura.buff = new_buff

    # Particles/VFX
    v = cfg.get("particle_speed", None)
    if v is not None:
        aura.particle_speed = v
    v = cfg.get("particle_colors", None)
    if v is not None:
        aura.particle_colors = v
    v = cfg.get("particle_lifespan", None)
    if v is not None:
        try:
            aura.particle_lifespan = int(v)
        except Exception:
            aura.particle_lifespan = v  # fallback as-is

    sr = cfg.get("size_range", None)
    if isinstance(sr, (list, tuple)) and len(sr) == 2:
        try:
            aura.particle_min_size = int(sr[0])
            aura.particle_max_size = int(sr[1])
        except Exception:
            # best-effort assignment
            aura.particle_min_size = sr[0]
            aura.particle_max_size = sr[1]

    v = cfg.get("emit_rate", None)
    if v is not None and hasattr(aura, "particles_per_frame"):
        try:
            aura.particles_per_frame = int(v)
        except Exception:
            aura.particles_per_frame = v

    # Optional overrides embedded in buff
    if isinstance(getattr(aura, "buff", None), dict):
        b = aura.buff
        v = b.get("offset_x", None)
        if v is not None:
            aura.offset_x = v
        v = b.get("particles_per_frame", None)
        if v is not None and hasattr(aura, "particles_per_frame"):
            try:
                aura.particles_per_frame = int(v)
            except Exception:
                aura.particles_per_frame = v
        v = b.get("particle_speed", None)
        if v is not None:
            aura.particle_speed = v
        v = b.get("particle_min_size", None)
        if v is not None:
            try:
                aura.particle_min_size = int(v)
            except Exception:
                aura.particle_min_size = v
        v = b.get("particle_max_size", None)
        if v is not None:
            try:
                aura.particle_max_size = int(v)
            except Exception:
                aura.particle_max_size = v
        v = b.get("particle_colors", None)
        if v is not None:
            aura.particle_colors = v
        v = b.get("particle_lifespan", None)
        if v is not None:
            try:
                aura.particle_lifespan = int(v)
            except Exception:
                aura.particle_lifespan = v


def log_aura_state(prefix: str, caster: Any, aura: Any, spell_key: Optional[str] = None, version: Optional[int] = None) -> None:
    """Unified debug logging for aura state to aid hot-reload troubleshooting."""
    try:
        heal_rate = 0
        if isinstance(getattr(aura, "buff", None), dict):
            heal_rate = aura.buff.get("heal_per_second", 0)
        logger.debug(
            "%s caster=%s key=%s ver=%s radius=%s duration=%s heal_per_second=%s ppf=%s p_speed=%s colors=%s size_range=(%s,%s)",
            prefix,
            caster,
            spell_key,
            version,
            getattr(aura, "radius", None),
            getattr(aura, "duration", None),
            heal_rate,
            getattr(aura, "particles_per_frame", None),
            getattr(aura, "particle_speed", None),
            getattr(aura, "particle_colors", None),
            getattr(aura, "particle_min_size", None),
            getattr(aura, "particle_max_size", None),
        )
    except Exception:
        # Do not let logging break gameplay
        pass
