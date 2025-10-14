from __future__ import annotations

import time
from dataclasses import dataclass
from typing import List, Tuple, Optional, Sequence

import pygame


Color = Tuple[int, int, int]
Point = Tuple[float, float]


@dataclass
class Marker:
    kind: str  # 'circle' | 'rect' | 'poly'
    color: Color
    label: Optional[str]
    t_end: float
    # payload
    circle: Optional[Tuple[float, float, float]] = None  # x, y, radius
    rect: Optional[Tuple[float, float, float, float]] = None  # x, y, w, h
    poly: Optional[List[Point]] = None


class MarkerRenderer:
    def __init__(self) -> None:
        self._markers: List[Marker] = []
        self._font: Optional[pygame.font.Font] = None
        self._ttl_default: float = 10.0

    # Add API
    def add_circle(self, x: float, y: float, radius: float, color: Color, label: Optional[str] = None, duration: Optional[float] = None) -> None:
        self._markers.append(
            Marker(
                kind='circle',
                color=color,
                label=label,
                t_end=time.time() + (duration if duration is not None else self._ttl_default),
                circle=(float(x), float(y), float(radius)),
            )
        )

    def add_rect(self, rect: pygame.Rect, color: Color, label: Optional[str] = None, duration: Optional[float] = None) -> None:
        self._markers.append(
            Marker(
                kind='rect',
                color=color,
                label=label,
                t_end=time.time() + (duration if duration is not None else self._ttl_default),
                rect=(float(rect.x), float(rect.y), float(rect.width), float(rect.height)),
            )
        )

    def add_poly(self, points_world: Sequence[Point], color: Color, label: Optional[str] = None, duration: Optional[float] = None) -> None:
        self._markers.append(
            Marker(
                kind='poly',
                color=color,
                label=label,
                t_end=time.time() + (duration if duration is not None else self._ttl_default),
                poly=[(float(x), float(y)) for (x, y) in points_world],
            )
        )

    # Render API
    def render(self, screen: pygame.Surface, camera) -> None:
        now = time.time()
        self._markers = [m for m in self._markers if m.t_end > now]
        if not self._markers:
            return
        self._ensure_font()
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        for m in self._markers:
            t_left = max(0.0, m.t_end - now)
            frac = min(1.0, t_left / self._ttl_default)
            r, g, b = m.color
            alpha = int(40 + 180 * frac)
            if m.kind == 'circle' and m.circle is not None:
                x, y, rad = m.circle
                sx, sy = camera.apply((x, y))
                rr = max(1, int(rad * camera.zoom))
                pygame.draw.circle(overlay, (r, g, b, alpha), (int(sx), int(sy)), rr, 2)
                if self._font and m.label:
                    txt = self._font.render(m.label, True, (r, g, b))
                    overlay.blit(txt, (int(sx) + 6, int(sy) - 6))
            elif m.kind == 'rect' and m.rect is not None:
                x, y, w, h = m.rect
                sx, sy = camera.apply((x, y))
                sw, sh = camera.scale((w, h))
                pygame.draw.rect(overlay, (r, g, b, alpha), pygame.Rect(int(sx), int(sy), int(sw), int(sh)), 2)
                if self._font and m.label:
                    txt = self._font.render(m.label, True, (r, g, b))
                    overlay.blit(txt, (int(sx) + 2, int(sy) - 12))
            elif m.kind == 'poly' and m.poly:
                if len(m.poly) >= 2:
                    pts_s = [camera.apply(p) for p in m.poly]
                    pygame.draw.lines(overlay, (r, g, b, alpha), True, pts_s, 2)
                    if self._font and m.label:
                        lx, ly = pts_s[0]
                        txt = self._font.render(m.label, True, (r, g, b))
                        overlay.blit(txt, (int(lx) + 2, int(ly) - 12))
        screen.blit(overlay, (0, 0))

    # Utils
    def _ensure_font(self) -> None:
        if self._font is None:
            try:
                self._font = pygame.font.SysFont(None, 14)
            except Exception:
                self._font = None
