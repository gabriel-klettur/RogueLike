from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, Optional, Union, TypedDict

from .enums import PreviewMode, SpellType


class VFXParticlesDict(TypedDict, total=False):
    # Free-form config; keep as dict[str, Any] but hint some common keys
    rate: float
    color: str
    duration_ms: int


@dataclass(slots=True)
class SpriteConfig:
    """Optional structured sprite configuration.

    The editor accepts either a raw path (flattened) or an object with fields.
    """

    path: str
    scale: Optional[float] = None

    def to_dict(self) -> Dict[str, Any]:
        out: Dict[str, Any] = {"path": self.path}
        if self.scale is not None:
            out["scale"] = self.scale
        return out


@dataclass(slots=True)
class VFXSpriteConfig:
    """Sprite configuration inside VFX config."""

    path: str
    scale: Optional[float] = None

    def to_dict(self) -> Dict[str, Any]:
        out: Dict[str, Any] = {"path": self.path}
        if self.scale is not None:
            out["scale"] = self.scale
        return out


@dataclass(slots=True)
class VFXConfig:
    """Visual effects configuration for a spell."""

    preview: Optional[PreviewMode] = None
    particles: Optional[Dict[str, Any]] = None
    sprite: Optional[VFXSpriteConfig] = None

    def to_dict(self) -> Dict[str, Any]:
        out: Dict[str, Any] = {}
        if self.preview is not None:
            out["preview"] = self.preview.value if isinstance(self.preview, PreviewMode) else str(self.preview)
        if self.particles is not None:
            out["particles"] = dict(self.particles)
        if self.sprite is not None:
            out["sprite"] = self.sprite.to_dict()
        return out


@dataclass(slots=True)
class SpellDefinition:
    """Data model for a Spell.

    Fields intentionally mirror the dynamic dict used across the editor so we can
    validate and serialize safely without changing existing JSON structure.
    """

    id: str
    type: Union[SpellType, str] = SpellType.GENERIC
    sprite: Optional[Union[str, SpriteConfig]] = None
    vfx: Optional[VFXConfig] = None
    # Allow extra fields to travel through (forward compatibility)
    extras: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        data: Dict[str, Any] = {"id": self.id}
        data["type"] = self.type.value if isinstance(self.type, SpellType) else str(self.type)
        if self.sprite is not None:
            if isinstance(self.sprite, SpriteConfig):
                # JSON currently expects a flattened string at root; keep both options compatible
                data["sprite"] = self.sprite.path
            else:
                data["sprite"] = self.sprite
        if self.vfx is not None:
            data["vfx"] = self.vfx.to_dict()
        if self.extras:
            data.update(self.extras)
        return data

    @staticmethod
    def from_dict(d: Dict[str, Any]) -> SpellDefinition:
        sid = str(d.get("id")) if d.get("id") is not None else ""
        st = d.get("type", SpellType.GENERIC)
        try:
            st_val: Union[SpellType, str]
            st_val = SpellType(st) if isinstance(st, str) and st in SpellType._value2member_map_ else st
        except Exception:
            st_val = str(st)
        sprite_val: Optional[Union[str, SpriteConfig]] = None
        spr = d.get("sprite")
        if isinstance(spr, str):
            sprite_val = spr
        elif isinstance(spr, dict):
            p = spr.get("path")
            if isinstance(p, str) and p:
                sprite_val = SpriteConfig(path=p, scale=spr.get("scale"))
        vfx_cfg: Optional[VFXConfig] = None
        vfx = d.get("vfx")
        if isinstance(vfx, dict):
            preview = vfx.get("preview")
            preview_mode: Optional[PreviewMode] = None
            if isinstance(preview, str) and preview in PreviewMode._value2member_map_:
                preview_mode = PreviewMode(preview)
            vfx_spr = vfx.get("sprite")
            vfx_sprite: Optional[VFXSpriteConfig] = None
            if isinstance(vfx_spr, dict):
                vp = vfx_spr.get("path")
                if isinstance(vp, str) and vp:
                    vfx_sprite = VFXSpriteConfig(path=vp, scale=vfx_spr.get("scale"))
            particles = vfx.get("particles") if isinstance(vfx.get("particles"), dict) else None
            vfx_cfg = VFXConfig(preview=preview_mode, particles=particles, sprite=vfx_sprite)
        extras: Dict[str, Any] = {}
        for k, v in d.items():
            if k not in {"id", "type", "sprite", "vfx"}:
                extras[k] = v
        return SpellDefinition(id=sid, type=st_val, sprite=sprite_val, vfx=vfx_cfg, extras=extras)
