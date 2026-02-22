from __future__ import annotations

import pygame
from typing import Any, Dict, Optional, Tuple

from roguelike_engine.utils.benchmark.benchmark import benchmark

from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.components.transform.scale import Scale


_DEFAULT_COLORS = {
    'water': (90, 180, 255),
    'poison': (40, 200, 60),
    'acid': (170, 220, 60),
    'lava': (255, 120, 60),
    'ice': (180, 230, 255),
}


class PuddleRenderSystem:
    """Renderiza charcos con caches para sprites y superficies circulares."""

    def __init__(
        self,
        perf_log: Optional[dict[str, list[float]]] = None,
    ) -> None:
        self.perf_log = perf_log
        self._scaled_surface_cache: Dict[tuple[int, float], pygame.Surface] = {}
        self._circle_cache: Dict[tuple[int, int, int, int, int], pygame.Surface] = {}
        self._ring_cache: Dict[tuple[int, int, int, int, int], pygame.Surface] = {}

    @benchmark(lambda self: self.perf_log, 'PuddleRenderSystem.update')
    def update(self, world: Any, screen: pygame.Surface, camera: Any) -> None:
        comps = world.components
        puddles = comps.get('PuddleComponent', {})
        if not puddles:
            return

        pos_map = comps.get('Position', {})
        sprite_map = comps.get('Sprite', {})
        scale_map = comps.get('Scale', {})

        screen_rect = screen.get_rect()
        zoom = float(getattr(camera, 'zoom', 1.0))
        cam_apply = getattr(camera, 'apply', lambda xy: xy)

        for eid, comp in puddles.items():
            pos = pos_map.get(eid)
            if pos is None:
                continue

            center = cam_apply((pos.x, pos.y))
            center_px = (int(center[0]), int(center[1]))

            radius_px = max(0, int(round(getattr(comp, 'radius', 0.0) * zoom)))
            did_draw = False

            sprite = sprite_map.get(eid)
            if sprite is not None and hasattr(sprite, 'image'):
                image = self._get_scaled_surface(sprite.image, scale_map.get(eid), zoom)
                rect = image.get_rect(center=center_px)
                if screen_rect.colliderect(rect):
                    screen.blit(image, rect.topleft)
                    did_draw = True

            if not did_draw:
                frames = getattr(comp, 'sequence_frames', None) or []
                if frames:
                    idx = int(getattr(comp, 'sequence_idx', 0))
                    if idx < 0 or idx >= len(frames):
                        idx = 0
                    frame = frames[idx]
                    image = self._get_scaled_surface(frame, scale_map.get(eid), zoom)
                    rect = image.get_rect(center=center_px)
                    if screen_rect.colliderect(rect):
                        screen.blit(image, rect.topleft)
                        did_draw = True

            if did_draw:
                if radius_px > 0:
                    ring_surface = self._get_ring_surface(radius_px, (255, 120, 0), 2)
                    ring_rect = ring_surface.get_rect(center=center_px)
                    if screen_rect.colliderect(ring_rect):
                        screen.blit(ring_surface, ring_rect.topleft)
                continue

            if radius_px <= 0:
                continue

            circle_surface = self._get_circle_surface(radius_px, comp)
            circle_rect = circle_surface.get_rect(center=center_px)
            if not screen_rect.colliderect(circle_rect):
                continue
            screen.blit(circle_surface, circle_rect.topleft)

    def _get_scaled_surface(
        self,
        source: pygame.Surface,
        scale_comp: Optional[Scale],
        zoom: float,
    ) -> pygame.Surface:
        entity_scale = float(getattr(scale_comp, 'scale', 1.0))
        scale_factor = entity_scale * zoom
        if abs(scale_factor - 1.0) <= 1e-3:
            return source

        key = (id(source), round(scale_factor, 4))
        cached = self._scaled_surface_cache.get(key)
        if cached is None:
            cached = pygame.transform.rotozoom(source, 0, scale_factor)
            self._scaled_surface_cache[key] = cached
        return cached

    def _get_circle_surface(self, radius_px: int, comp: PuddleComponent) -> pygame.Surface:
        color = self._resolve_color(comp)
        alpha = max(0, min(255, int(getattr(comp, 'alpha', 160))))
        key = (radius_px, color[0], color[1], color[2], alpha)
        cached = self._circle_cache.get(key)
        if cached is None:
            diameter = radius_px * 2
            cached = pygame.Surface((diameter, diameter), pygame.SRCALPHA)
            pygame.draw.circle(cached, (*color, alpha), (radius_px, radius_px), radius_px)
            self._circle_cache[key] = cached
        return cached

    def _get_ring_surface(self, radius_px: int, color: Tuple[int, int, int], width: int) -> pygame.Surface:
        key = (radius_px, color[0], color[1], color[2], width)
        cached = self._ring_cache.get(key)
        if cached is None:
            diameter = radius_px * 2
            cached = pygame.Surface((diameter, diameter), pygame.SRCALPHA)
            pygame.draw.circle(cached, (*color, 220), (radius_px, radius_px), radius_px, width)
            self._ring_cache[key] = cached
        return cached

    @staticmethod
    def _resolve_color(comp: PuddleComponent) -> Tuple[int, int, int]:
        color = getattr(comp, 'color', None)
        if color is not None:
            return tuple(color)
        element = (getattr(comp, 'element', None) or '').lower()
        return _DEFAULT_COLORS.get(element, (120, 200, 220))
