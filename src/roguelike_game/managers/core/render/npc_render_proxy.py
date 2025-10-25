import pygame
from typing import Tuple

# Optional NumPy path for higher-quality tinting
try:
    import numpy as np
    from pygame import surfarray
    HAS_NUMPY = True
except Exception:  # pragma: no cover - numpy is optional
    HAS_NUMPY = False


class _NPCWrapper:
    """Lightweight proxy to render ECS NPCs consistently.

    Expects ECS world with Position, Sprite and optional Scale components.
    Provides caching for scaled and tinted surfaces.
    """

    __slots__ = ("world", "eid", "pos_map", "sprite_map", "scale_map")

    # Cache of scaled surfaces: {(eid, scale, id(orig)): Surface}
    _scale_cache: dict[tuple[int, float, int], pygame.Surface] = {}
    # Cache of tinted surfaces: {(id(image), r, g, b): Surface}
    _tinted_cache: dict[tuple[int, int, int, int], pygame.Surface] = {}

    def __init__(self, world, eid: int) -> None:
        self.world = world
        comps = world.components
        self.eid = eid
        self.pos_map = comps["Position"]
        self.sprite_map = comps["Sprite"]
        self.scale_map = comps.get("Scale", {})

    @property
    def x(self) -> int:
        return self.pos_map[self.eid].x

    @property
    def y(self) -> int:
        return self.pos_map[self.eid].y

    def render(self, screen: pygame.Surface, camera) -> None:
        blit = screen.blit
        apply = camera.apply
        eid = self.eid

        sprite = self.sprite_map[eid]
        orig = sprite.image
        scale_comp = self.scale_map.get(eid)
        entity_scale = scale_comp.scale if scale_comp else 1.0
        scale_factor = entity_scale * camera.zoom

        if scale_factor != 1.0:
            key = (eid, round(scale_factor, 2), id(orig))
            image = _NPCWrapper._scale_cache.get(key)
            if image is None:
                w, h = orig.get_size()
                image = pygame.transform.scale(orig, (int(w * scale_factor), int(h * scale_factor)))
                _NPCWrapper._scale_cache[key] = image
        else:
            image = orig

        # Optional golden tint when player is in godmode
        try:
            is_player = eid == getattr(self.world, "player_entity", None)
            godmode = bool(getattr(getattr(self.world, "state", None), "godmode", False))
        except Exception:
            is_player = False
            godmode = False
        if is_player and godmode:
            color = (255, 230, 100)
            tkey = (id(image), color[0], color[1], color[2])
            tinted = _NPCWrapper._tinted_cache.get(tkey)
            if tinted is None:
                tinted = self._tint_surface(image, color)
                _NPCWrapper._tinted_cache[tkey] = tinted
            image = tinted

        # Red tint blink when burning
        try:
            burns = self.world.components.get('BurnComponent', {})
            burn = burns.get(eid)
        except Exception:
            burn = None
        if burn is not None:
            try:
                import time as _t
                start = float(getattr(burn, 'start_time', 0.0))
                tick = float(getattr(burn, 'tick_period', 1.0)) or 1.0
                elapsed = max(0.0, _t.time() - start)
                blink_interval = max(0.1, min(0.25, tick / 2.0))
                if int(elapsed / blink_interval) % 2 == 0:
                    color = (255, 64, 64)
                    tkey = (id(image), color[0], color[1], color[2])
                    tinted = _NPCWrapper._tinted_cache.get(tkey)
                    if tinted is None:
                        tinted = self._tint_surface(image, color)
                        _NPCWrapper._tinted_cache[tkey] = tinted
                    image = tinted
            except Exception:
                pass

        blit(image, apply((self.x, self.y)))

    @staticmethod
    def _tint_surface(surface: pygame.Surface, color: Tuple[int, int, int]) -> pygame.Surface:
        """Apply color tint preserving alpha.
        With NumPy: tint by luminance for a consistent result.
        Without NumPy: fallback to blend ops.
        """
        try:
            if HAS_NUMPY:
                rgb = surfarray.array3d(surface).astype("float32")
                lum = 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]
                r_fac, g_fac, b_fac = color[0] / 255.0, color[1] / 255.0, color[2] / 255.0
                new_rgb = np.zeros_like(rgb)
                new_rgb[:, :, 0] = np.clip(lum * r_fac, 0, 255)
                new_rgb[:, :, 1] = np.clip(lum * g_fac, 0, 255)
                new_rgb[:, :, 2] = np.clip(lum * b_fac, 0, 255)
                new_rgb = new_rgb.astype("uint8")
                out = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
                surfarray.blit_array(out, new_rgb)
                try:
                    src_a = surfarray.array_alpha(surface)
                    dst_a = surfarray.pixels_alpha(out)
                    dst_a[:, :] = src_a
                    del dst_a
                except Exception:
                    pass
                return out
            else:
                img = surface.copy()
                tint = pygame.Surface(img.get_size(), pygame.SRCALPHA)
                tint.fill((color[0], color[1], color[2], 255))
                img.blit(tint, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
                boost = pygame.Surface(img.get_size(), pygame.SRCALPHA)
                boost.fill((40, 35, 0, 0))
                img.blit(boost, (0, 0), special_flags=pygame.BLEND_RGBA_ADD)
                return img
        except Exception:
            return surface
