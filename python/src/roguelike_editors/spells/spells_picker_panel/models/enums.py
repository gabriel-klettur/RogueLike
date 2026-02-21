from __future__ import annotations

from enum import Enum


class SpellType(str, Enum):
    """Canonical spell type identifiers used across the editor and runtime."""

    GENERIC = "generic"
    LIGHTNING = "lightning"
    AURA = "aura"
    BEAM = "beam"
    DASH = "dash"
    SLASH = "slash"
    ARCANE_FLAME = "arcane_flame"
    FIREWORK = "firework"
    FIREWORK_LAUNCH = "firework_launch"
    SMOKE_EMITTER = "smoke_emitter"
    SMOKE = "smoke"
    TELEPORT = "teleport"
    SPHERE_MAGIC_SHIELD = "sphere_magic_shield"


class PreviewMode(str, Enum):
    """Preview rendering strategy for the picker/properties panel."""

    SPRITE = "sprite"      # render static/animated sprite (default)
    PARTICLES = "particles" # use particles preview provider
    NONE = "none"           # no preview available
