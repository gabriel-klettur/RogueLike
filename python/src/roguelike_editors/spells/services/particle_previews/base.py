from __future__ import annotations

import pygame
from typing import Tuple


class DummyCamera:
    def __init__(self, w: int | None = None, h: int | None = None):
        self.w = w
        self.h = h

    def apply(self, pos: Tuple[float, float]) -> Tuple[int, int]:
        return int(pos[0]), int(pos[1])

    def is_in_view(self, x: float, y: float, size: Tuple[int, int]) -> bool:
        if self.w is None or self.h is None:
            return True
        sw, sh = size
        return (-sw <= x < self.w and -sh <= y < self.h)


def eval_curve(curve, t: float, default: float) -> float:
    if not isinstance(curve, (list, tuple)) or len(curve) == 0:
        return float(default)
    pts: list[tuple[float, float]] = []
    for pt in curve:
        try:
            pts.append((float(pt[0]), float(pt[1])))
        except Exception:
            continue
    if not pts:
        return float(default)
    pts.sort(key=lambda x: x[0])
    if t <= pts[0][0]:
        return pts[0][1]
    if t >= pts[-1][0]:
        return pts[-1][1]
    for i in range(1, len(pts)):
        t0, v0 = pts[i - 1]
        t1, v1 = pts[i]
        if t0 <= t <= t1 and t1 > t0:
            k = (t - t0) / (t1 - t0)
            return v0 * (1 - k) + v1 * k
    return float(default)


def eval_color_gradient(grad, t: float, base: Tuple[int, int, int]) -> Tuple[int, int, int]:
    if not isinstance(grad, (list, tuple)) or len(grad) == 0:
        return base
    pts: list[tuple[float, Tuple[int, int, int]]] = []
    for pt in grad:
        try:
            col = pt[1]
            if isinstance(col, (list, tuple)) and len(col) >= 3:
                pts.append((float(pt[0]), (int(col[0]), int(col[1]), int(col[2]))))
        except Exception:
            continue
    if not pts:
        return base
    pts.sort(key=lambda x: x[0])
    if t <= pts[0][0]:
        return pts[0][1]
    if t >= pts[-1][0]:
        return pts[-1][1]
    for i in range(1, len(pts)):
        t0, c0 = pts[i - 1]
        t1, c1 = pts[i]
        if t0 <= t <= t1 and t1 > t0:
            k = (t - t0) / (t1 - t0)
            r = int(c0[0] * (1 - k) + c1[0] * k)
            g = int(c0[1] * (1 - k) + c1[1] * k)
            b = int(c0[2] * (1 - k) + c1[2] * k)
            return (r, g, b)
    return base


class TextureFlipbookHelper:
    def __init__(self, texture_path: str | None = None, flipbook: dict | None = None) -> None:
        self._tex_path = texture_path if isinstance(texture_path, str) else None
        self._flipbook = dict(flipbook) if isinstance(flipbook, dict) else None
        self._sheet: pygame.Surface | None = None
        self._frame_cache: dict[tuple[int, int], pygame.Surface] = {}

    def _ensure_sheet(self) -> None:
        if self._sheet is None and isinstance(self._tex_path, str):
            try:
                img = pygame.image.load(self._tex_path)
                self._sheet = img.convert_alpha()
            except Exception:
                self._sheet = None

    def _get_frame(self, t: float, size_px: int) -> pygame.Surface | None:
        self._ensure_sheet()
        if self._sheet is None:
            return None
        sheet = self._sheet
        fb = self._flipbook
        if isinstance(fb, dict):
            sw, sh = sheet.get_size()
            cols = int(fb.get("cols", 1) or 1)
            rows = int(fb.get("rows", 1) or 1)
            total = int(fb.get("total", max(1, cols * rows)) or max(1, cols * rows))
            fw = int(fb.get("frame_w", sw // max(1, cols)))
            fh = int(fb.get("frame_h", sh // max(1, rows)))
            loop = bool(fb.get("loop", True))
            idx = int(min(0.999, max(0.0, t)) * total)
            if loop and total > 0:
                idx = idx % total
            idx = max(0, min(total - 1, idx)) if total > 0 else 0
            col = idx % cols
            row = idx // cols
            rx = col * fw
            ry = row * fh
            rect = pygame.Rect(rx, ry, fw, fh)
        else:
            rect = sheet.get_rect()
        key = (hash((rect.x, rect.y, rect.w, rect.h)), int(size_px))
        frm = self._frame_cache.get(key)
        if frm is None:
            try:
                raw = sheet.subsurface(rect).copy()
            except Exception:
                raw = sheet.copy()
            if (raw.get_width(), raw.get_height()) != (size_px, size_px):
                try:
                    raw = pygame.transform.smoothscale(raw, (size_px, size_px))
                except Exception:
                    raw = pygame.transform.scale(raw, (size_px, size_px))
            frm = raw
            if len(self._frame_cache) > 256:
                self._frame_cache.clear()
            self._frame_cache[key] = frm
        return frm
