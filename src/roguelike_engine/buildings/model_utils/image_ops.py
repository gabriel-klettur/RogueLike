from __future__ import annotations

from typing import Callable, Dict, Optional, Tuple
import pygame

# Cache for building images: key = (image_path, scale)
_BUILDING_IMAGE_CACHE: Dict[Tuple[str, Optional[Tuple[int, int]]], pygame.Surface] = {}


def clear_building_image_cache() -> None:
    """Clear the internal cache used for building images.

    Intended primarily for tests to ensure deterministic image sizes when the
    image loader is monkeypatched per-test.
    """
    _BUILDING_IMAGE_CACHE.clear()


def load_and_prepare_image(
    image_path: str,
    scale: Optional[tuple[int, int]] | None,
    *,
    loader: Callable[[str], pygame.Surface],
) -> tuple[pygame.Surface, tuple[int, int]]:
    """Load an image and apply an optional scale with an internal cache.

    Returns the resulting surface and the applied size as (w, h).
    Uses a 1/4 downscale heuristic for very large images if no explicit scale.
    The image is loaded via the provided `loader` callback to support tests
    that monkeypatch the symbol in the calling module.
    """
    key = (image_path, scale)
    if key in _BUILDING_IMAGE_CACHE:
        surf = _BUILDING_IMAGE_CACHE[key]
        applied_size = surf.get_size()
        return surf, applied_size

    raw = loader(image_path)
    if scale:
        surf = pygame.transform.scale(raw, scale)
        applied_size = scale
    else:
        w, h = raw.get_size()
        if w > 512 or h > 512:
            new_size = (w // 4, h // 4)
            surf = pygame.transform.scale(raw, new_size)
            applied_size = new_size
        else:
            surf = raw
            applied_size = (w, h)

    _BUILDING_IMAGE_CACHE[key] = surf
    return surf, applied_size


def build_full_mask(surface: pygame.Surface) -> pygame.Mask:
    """Create a pygame.Mask from the full image alpha."""
    return pygame.mask.from_surface(surface)
