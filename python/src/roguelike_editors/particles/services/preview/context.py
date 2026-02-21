"""Utilities for constructing particle preview context objects."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Sequence, Tuple

Color = Tuple[int, int, int]
Palette = Optional[Sequence[Color]]


def _coerce_color(value: Sequence[Any]) -> Optional[Color]:
    try:
        if len(value) < 3:
            return None
        return (int(value[0]), int(value[1]), int(value[2]))
    except (TypeError, ValueError):
        return None


def _collect_palette(values: Sequence[Any]) -> List[Color]:
    palette: List[Color] = []
    for item in values:
        if isinstance(item, Sequence):
            color = _coerce_color(item)
            if color is not None:
                palette.append(color)
    return palette


@dataclass(frozen=True)
class ParticlePreviewContext:
    """Holds normalized data required to build a particle preview.

    Attributes:
        definition: Raw spell or preset definition dictionary.
        parts: Normalized particle configuration extracted from the definition.
        meta: Auxiliary metadata emitted by the resolver (e.g., preset id).
        color: Primary explicit color if the config provides one.
        color_explicit: Indicates whether *color* was explicitly specified.
        palette: Optional list of colors extracted from the configuration.
    """

    definition: Dict[str, Any]
    parts: Dict[str, Any]
    meta: Dict[str, Any]
    color: Optional[Color]
    color_explicit: bool
    palette: Optional[List[Color]]

    def color_or(self, fallback: Color) -> Color:
        return self.color if self.color_explicit and self.color is not None else fallback

    def palette_or_none(self) -> Optional[Sequence[Color]]:
        return self.palette if self.palette else None

    def get_number(self, key: str, default: Optional[float] = None) -> Optional[float]:
        value = self.parts.get(key)
        if isinstance(value, (int, float)):
            return float(value)
        return default

    def get_int(self, key: str, default: Optional[int] = None) -> Optional[int]:
        value = self.parts.get(key)
        if isinstance(value, int):
            return int(value)
        if isinstance(value, float):
            return int(value)
        return default

    def get_tuple(self, key: str) -> Optional[Tuple[Any, ...]]:
        value = self.parts.get(key)
        if isinstance(value, Sequence):
            return tuple(value)
        return None

    def get_dict(self, key: str) -> Optional[Dict[str, Any]]:
        value = self.parts.get(key)
        if isinstance(value, dict):
            return value
        return None


def build_context(definition: Dict[str, Any], parts: Dict[str, Any], meta: Dict[str, Any]) -> ParticlePreviewContext:
    """Create a :class:`ParticlePreviewContext` gathering color and palette data."""

    color: Optional[Color] = None
    color_explicit = False
    palette: Optional[List[Color]] = None

    try:
        raw_color = parts.get("color")
        if isinstance(raw_color, Sequence):
            inferred = _coerce_color(raw_color)
            if inferred is not None:
                color = inferred
                color_explicit = True
        if not color_explicit:
            raw_colors = parts.get("colors")
            if isinstance(raw_colors, Sequence) and raw_colors:
                first_color = _coerce_color(raw_colors[0]) if isinstance(raw_colors[0], Sequence) else None
                if first_color is not None:
                    color = first_color
                    color_explicit = True
                    palette = _collect_palette(raw_colors)
    except Exception:
        # Color detection should never abort preview building.
        pass

    return ParticlePreviewContext(
        definition=definition,
        parts=parts,
        meta=meta,
        color=color,
        color_explicit=color_explicit,
        palette=palette,
    )
