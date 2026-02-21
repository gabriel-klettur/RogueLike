from __future__ import annotations

from typing import Any, Dict, List, Tuple, Union

from .enums import PreviewMode, SpellType
from .spell_definition import SpellDefinition, SpriteConfig, VFXConfig, VFXSpriteConfig


def validate_spell_definition(sd: SpellDefinition) -> Tuple[bool, List[str]]:
    """Validate a SpellDefinition instance.

    Returns a tuple (is_valid, errors).
    Rules are conservative to preserve current JSON flexibility:
      - "type" accepts any string, but non-string and non-SpellType is invalid.
      - "sprite" is optional; if provided must be non-empty string or SpriteConfig with non-empty path.
      - "vfx.preview" accepts PreviewMode or a valid string from PreviewMode; other strings are invalid.
      - "vfx.particles" if provided must be a dict.
      - "vfx.sprite" if provided must have non-empty path.
    """
    errors: List[str] = []

    # id
    if not isinstance(sd.id, str) or not sd.id.strip():
        errors.append("id: must be a non-empty string")

    # type
    if not (isinstance(sd.type, SpellType) or isinstance(sd.type, str)):
        errors.append("type: must be a SpellType or string")

    # sprite
    if sd.sprite is not None:
        if isinstance(sd.sprite, str):
            if not sd.sprite.strip():
                errors.append("sprite: non-empty string expected when provided")
        elif isinstance(sd.sprite, SpriteConfig):
            if not isinstance(sd.sprite.path, str) or not sd.sprite.path.strip():
                errors.append("sprite.path: non-empty string expected")
        else:
            errors.append("sprite: must be str or SpriteConfig when provided")

    # vfx
    if sd.vfx is not None:
        vfx: VFXConfig = sd.vfx
        # preview
        if vfx.preview is not None:
            if isinstance(vfx.preview, PreviewMode):
                pass
            elif isinstance(vfx.preview, str):
                if vfx.preview not in PreviewMode._value2member_map_:
                    errors.append(
                        f"vfx.preview: invalid value '{vfx.preview}', expected one of {list(PreviewMode._value2member_map_.keys())}"
                    )
            else:
                errors.append("vfx.preview: must be PreviewMode or string value")
        # particles
        if vfx.particles is not None and not isinstance(vfx.particles, dict):
            errors.append("vfx.particles: must be a dict when provided")
        # sprite
        if vfx.sprite is not None:
            vfxs: VFXSpriteConfig = vfx.sprite
            if not isinstance(vfxs.path, str) or not vfxs.path.strip():
                errors.append("vfx.sprite.path: non-empty string expected")

    return (len(errors) == 0, errors)


def validate_spell_definition_dict(d: Dict[str, Any]) -> Tuple[bool, SpellDefinition, List[str]]:
    """Validate a raw dictionary describing a spell.

    Returns (is_valid, normalized_dataclass, errors).
    """
    sd = SpellDefinition.from_dict(d)
    ok, errors = validate_spell_definition(sd)
    return ok, sd, errors
