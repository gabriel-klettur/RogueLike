from __future__ import annotations

import json
from typing import Any, Dict, List, Tuple


def extract_data_map_to_dict(spell_obj: Any) -> Dict[str, Any]:
    """Normalize a spell object into a plain dict for rendering.

    Tries common access patterns (dict, pydantic-like, __dict__).
    """
    if spell_obj is None:
        return {}
    if isinstance(spell_obj, dict):
        return spell_obj
    if hasattr(spell_obj, "model_dump"):
        try:
            return spell_obj.model_dump()
        except Exception:
            pass
    try:
        return spell_obj.dict()  # type: ignore[attr-defined]
    except Exception:
        try:
            return vars(spell_obj)
        except Exception:
            return {}


def get_by_path(d: Dict[str, Any], path: str, default: Any = "") -> Any:
    """Get nested values using dot-path with legacy fallbacks.

    Includes backward-compat mappings for previous flat spell formats.
    """
    cur: Any = d
    for part in path.split("."):
        if not isinstance(cur, dict) or part not in cur:
            cur = None
            break
        cur = cur[part]
    if cur is None:
        # Legacy fallbacks
        if path == "vfx.sprite.path":
            return d.get("sprite", default)
        if path == "vfx.sprite.scale":
            return d.get("scale", default)
        fb_map = {
            "vfx.particles.count": "particle_count",
            "vfx.particles.dispersion": "particle_dispersion",
            "vfx.particles.colors": "particle_colors",
            "vfx.particles.lifespan": "particle_lifespan",
            "vfx.particles.speed": "particle_speed",
        }
        if path in fb_map:
            return d.get(fb_map[path], default)
        return default
    return cur


def fmt_val(v: Any) -> str:
    """Format a value for display, preserving JSON for dict/list."""
    if isinstance(v, (dict, list)):
        try:
            return json.dumps(v, ensure_ascii=False)
        except Exception:
            return str(v)
    if v is None:
        return ""
    return str(v)


def build_ordered_keys() -> List[str]:
    """Return the ordered list of property paths to show in the view."""
    keys: List[str] = []
    keys += ["id", "name", "type"]
    keys += [
        "timings.prepare",
        "timings.channel",
        "timings.cooldown",
    ]
    keys += ["mana_cost"]
    keys += [
        "rules.allow_movement",
        "rules.lock_cast_direction",
        "rules.interruptible",
        "rules.automatic",
        "rules.automatic_cast_punish",
    ]
    keys += [
        "constraints.max_instances",
        "constraints.allow_overlap",
    ]
    keys += [
        "effect.damage",
        "effect.range",
        "effect.speed",
        "effect.duration",
        "effect.lifetime",
        "effect.radius",
        "effect.distance",
        "effect.arc_range_degrees",
        "effect.buff",
    ]
    keys += [
        "vfx.preset",
        "vfx.sprite.path",
        "vfx.sprite.scale",
        "vfx.particles.count",
        "vfx.particles.dispersion",
        "vfx.particles.colors",
        "vfx.particles.lifespan",
        "vfx.particles.speed",
        "vfx.particles.size_range",
        "vfx.particles.color",
        "vfx.particles.emit_rate",
    ]
    keys += [
        "meta.offset",
        "meta.speed_multiplier",
        "meta.segments",
    ]
    return keys


def build_entries(
    data_map: Dict[str, Any],
    editing_property: str | None,
    editing_text: str | None,
) -> List[Tuple[str, str]]:
    """Build (key, value) entries for the properties list with edit state."""
    entries: List[Tuple[str, str]] = []
    for k in build_ordered_keys():
        raw_val = get_by_path(data_map, k, "") if "." in k else data_map.get(k, "")
        display_val = editing_text if (editing_property == k) else fmt_val(raw_val)
        entries.append((k, display_val))
    return entries


def infer_particle_kind(d: Dict[str, Any]) -> str:
    """Infer a friendly name for the particle system from spell data."""
    try:
        vfx_local = d.get("vfx", {}) if isinstance(d.get("vfx", {}), dict) else {}
        parts = vfx_local.get("particles", {}) if isinstance(vfx_local.get("particles", {}), dict) else {}
        kind_local = parts.get("kind") if isinstance(parts, dict) else None
        if kind_local:
            return str(kind_local)
        stype = d.get("type")
        if stype in ("aura",):
            return "aura"
        if stype in ("beam",):
            return "laser"
        if stype in ("dash",):
            return "dash"
        if stype in ("slash",):
            return "slash"
        if stype in ("lightning",):
            return "lightning"
        if stype in ("arcane_flame",):
            return "arcane_flame"
        if stype in ("firework", "firework_launch"):
            return "firework"
        if stype in ("smoke_emitter",):
            return "smoke_emitter"
        if stype in ("smoke",):
            return "smoke"
        if stype in ("teleport",):
            return "teleport"
        if stype in ("sphere_magic_shield",):
            return "aura"
        sid_l = str(d.get("id") or "").lower()
        for kw, kind_m in (
            ("aura", "aura"),
            ("beam", "laser"),
            ("laser", "laser"),
            ("dash", "dash"),
            ("slash", "slash"),
            ("lightning", "lightning"),
            ("firework", "firework"),
            ("smoke_emitter", "smoke_emitter"),
            ("smoke", "smoke"),
            ("flame", "arcane_flame"),
            ("teleport", "teleport"),
            ("shield", "aura"),
        ):
            if kw in sid_l:
                return kind_m
    except Exception:
        pass
    return "particles"
