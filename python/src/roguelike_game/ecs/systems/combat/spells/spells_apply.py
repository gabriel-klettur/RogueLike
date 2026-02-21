import logging
from typing import Any, Dict, List, Tuple, Optional

# Centralized particles catalog (for presets referenced by spells)
from roguelike_game.config.particles_config import get_preset

# Type-only import to avoid heavy coupling
from roguelike_game.config.spells_config import SpellConfig

logger = logging.getLogger(__name__)


def _resolve_effective_particles(defn: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    """Return merged particles dict from a spell definition using presets when present.

    Priority: preset defaults <- overrides in defn.vfx.particles.
    Returns None when no particles info is available at all.
    """
    try:
        vfx = defn.get("vfx")
        base: Dict[str, Any] = {}
        overrides: Dict[str, Any] = {}
        # New style nested object
        if isinstance(vfx, dict):
            preset_id = vfx.get("preset") if isinstance(vfx.get("preset"), str) else None
            if preset_id:
                p = get_preset(preset_id)
                if p and isinstance(p.vfx, dict):
                    pv = p.vfx.get("particles")
                    if isinstance(pv, dict):
                        base = dict(pv)
            pov = vfx.get("particles")
            if isinstance(pov, dict):
                overrides = dict(pov)
        # Legacy: vfx is a string preset id
        elif isinstance(vfx, str):
            p = get_preset(vfx)
            if p and isinstance(p.vfx, dict):
                pv = p.vfx.get("particles")
                if isinstance(pv, dict):
                    base = dict(pv)
        if not base and not overrides:
            return None
        eff = {**base, **overrides}
        # Normalize color/colors
        if "color" in eff and "colors" not in eff:
            c = eff.get("color")
            if isinstance(c, (list, tuple)) and len(c) >= 3:
                eff["colors"] = [list(c)[:3]]
        return eff
    except Exception:
        return None


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

    # Particles/VFX (prefer preset+overrides when present; fallback to flat keys)
    effective = _resolve_effective_particles(cfg.extra if isinstance(getattr(cfg, "extra", {}), dict) else {})
    if effective is None:
        # Fallback to flat fields on cfg (legacy path)
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
    else:
        # Apply from effective dict
        spd = effective.get("speed")
        if isinstance(spd, (int, float)):
            aura.particle_speed = spd
        cols = effective.get("colors")
        if isinstance(cols, (list, tuple)) and len(cols) > 0:
            aura.particle_colors = cols
        life = effective.get("lifespan")
        if isinstance(life, (int, float)):
            try:
                aura.particle_lifespan = int(life)
            except Exception:
                aura.particle_lifespan = life
        sr = effective.get("size_range")
        if isinstance(sr, (list, tuple)) and len(sr) == 2:
            try:
                aura.particle_min_size = int(sr[0])
                aura.particle_max_size = int(sr[1])
            except Exception:
                aura.particle_min_size = sr[0]
                aura.particle_max_size = sr[1]
        er = effective.get("emit_rate")
        if er is None:
            cnt = effective.get("count")
            if isinstance(cnt, int) and cnt > 0:
                er = max(1, min(8, cnt // 2))
        if er is not None and hasattr(aura, "particles_per_frame"):
            try:
                aura.particles_per_frame = int(er)
            except Exception:
                aura.particles_per_frame = er

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
