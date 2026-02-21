"""Utility functions supporting spell release handlers."""

from __future__ import annotations

import logging
import math
from typing import Any, Iterable, List, Sequence, Tuple

import pygame

logger = logging.getLogger(__name__)


def load_image_safe(sprite_path: str) -> Any | None:
    """Load a Pygame surface without requiring an initialized display."""

    try:
        image = pygame.image.load(sprite_path)
    except Exception:  # pragma: no cover - matches legacy resilience
        return None

    try:
        display_ready = pygame.display.get_init() and pygame.display.get_surface() is not None
    except Exception:  # pragma: no cover
        display_ready = False

    if display_ready:
        try:
            return image.convert_alpha()
        except Exception:  # pragma: no cover
            return image
    return image


def normalise_vector(vector: Sequence[float], fallback: Tuple[float, float] = (1.0, 0.0)) -> Tuple[float, float]:
    """Return a normalised two dimensional vector."""

    if len(vector) < 2:
        return fallback
    try:
        dx, dy = float(vector[0]), float(vector[1])
    except (TypeError, ValueError):
        return fallback
    length = math.hypot(dx, dy)
    if length <= 1e-12:
        return fallback
    return dx / length, dy / length


def radial_directions(count: int, start_deg: float) -> List[Tuple[float, float]]:
    """Generate unit vectors arranged radially around a circle."""

    if count <= 0:
        return []
    step = 360.0 / float(count)
    return [
        (math.cos(math.radians(start_deg + index * step)), math.sin(math.radians(start_deg + index * step)))
        for index in range(count)
    ]


def ensure_iterable(value: Any) -> Iterable[Any]:
    """Turn *value* into a safe iterable."""

    if value is None:
        return []
    if isinstance(value, (list, tuple, set)):
        return value
    return [value]


def enqueue_audio_event(world: Any, event: dict[str, Any]) -> None:
    """Queue an audio event in the world's shared event buffer."""

    if world is None:
        return
    try:
        queue = world.components.setdefault("AudioEventQueue", [])  # type: ignore[attr-defined]
        queue.append(event)
    except Exception:  # pragma: no cover - preserve fault tolerance
        logger.debug("Failed to enqueue audio event", exc_info=True)


def coerce_float(value: Any, default: float) -> float:
    """Best-effort conversion to ``float`` with fallback."""

    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def coerce_int(value: Any, default: int) -> int:
    """Best-effort conversion to ``int`` with fallback."""

    try:
        return int(value)
    except (TypeError, ValueError):
        return default
