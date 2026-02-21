from __future__ import annotations

from functools import lru_cache
import pygame

@lru_cache(maxsize=128)
def get_radial_gradient(radius: int, falloff: float = 2.0, center_scale: float = 1.0) -> pygame.Surface:
    """Return a cached white radial gradient Surface (SRCALPHA).

    - Center is 100% (white), edge is 0%.
    - Alpha falls off with exponent=falloff.
    - Intended to be tinted per-light via BLEND_RGBA_MULT and then blitted with BLEND_RGBA_ADD.
    """
    r = max(1, int(radius))
    size = r * 2
    surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
    px = pygame.surfarray.pixels_alpha(surf)
    rgb = pygame.surfarray.pixels3d(surf)
    cx = cy = r
    rf = float(r)
    fo = max(0.1, float(falloff))
    cs = max(0.1, min(2.0, float(center_scale)))
    # Compute alpha radial falloff; use integer loops for speed
    for y in range(size):
        dy = y - cy
        for x in range(size):
            dx = x - cx
            d = (dx * dx + dy * dy) ** 0.5
            t = 0.0 if d >= rf else (1.0 - (d / rf))
            # Sharpen/soften with exponent; shape center intensity by center_scale.
            # scale_factor transitions from cs at center (t=1) to 1.0 at edge (t=0)
            scale_factor = 1.0 - (1.0 - cs) * t
            a = int(max(0.0, min(1.0, (t ** fo) * scale_factor)) * 255)
            px[x, y] = a
            # Encode intensity into RGB so additive blending uses the radial falloff
            rgb[x, y, 0] = a
            rgb[x, y, 1] = a
            rgb[x, y, 2] = a
    del px
    del rgb
    return surf
