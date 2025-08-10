"""
Reusable Picker Panel widget for grid-based item picking.

Features:
- Draggable floating panel (RMB drag)
- Scrollable grid layout with auto column calculation
- Hover and selection states
- Keyboard navigation (arrows, Enter)
- Callback hooks to render items and react to selection/open actions

Intended to unify Entities and Tiles editors' picker panels.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable, Optional, Tuple, List

import pygame as pg


Rect = pg.Rect
Color = Tuple[int, int, int]


@dataclass
class PickerPanelState:
    """State container shared between controller/view/event code and the widget.

    Keep this small and serializable. External editors can keep their own domain state
    and only mirror the fields they need here.
    """

    rect: Rect
    visible: bool = True
    scroll_y: int = 0
    hovered_index: Optional[int] = None
    selected_index: Optional[int] = None
    dragging: bool = False
    drag_offset: Tuple[int, int] = (0, 0)

    # Computed layout/cache
    content_height: int = 0
    item_rects: List[Rect] = field(default_factory=list)


class _DoubleClickTracker:
    def __init__(self, interval_ms: int = 300) -> None:
        self.interval_ms = interval_ms
        self._last_time = 0
        self._last_index: Optional[int] = None

    def is_double_click(self, index: Optional[int]) -> bool:
        now = pg.time.get_ticks()
        if index is not None and self._last_index == index and (now - self._last_time) <= self.interval_ms:
            self._last_time = 0
            self._last_index = None
            return True
        self._last_time = now
        self._last_index = index
        return False


class PickerPanel:
    """Reusable grid picker panel.

    Usage:
        state = PickerPanelState(rect=pg.Rect(40, 40, 360, 360))
        panel = PickerPanel(cell_size=(64, 64))
        panel.set_item_count(lambda: len(items))
        panel.set_draw_item(draw_item)
        panel.on_select = on_select
        panel.on_open = on_open
        # in loop: panel.handle_event(event, state) then panel.render(screen, state)
    """

    def __init__(
        self,
        *,
        cell_size: Tuple[int, int] = (64, 64),
        margin: int = 8,
        padding: int = 8,
        bg_color: Color = (18, 18, 18),
        border_color: Color = (90, 90, 90),
        hover_color: Color = (255, 255, 0),
        select_color: Color = (0, 200, 255),
        grid_bg_color: Optional[Color] = None,
        draw_panel_bg: bool = True,
        allow_dragging: bool = True,
        max_columns: Optional[int] = None,
        draw_overlays: bool = True,
    ) -> None:
        self.cell_w, self.cell_h = cell_size
        self.margin = margin
        self.padding = padding
        self.bg_color = bg_color
        self.border_color = border_color
        self.hover_color = hover_color
        self.select_color = select_color
        self.grid_bg_color = grid_bg_color
        self.draw_panel_bg = draw_panel_bg
        self.allow_dragging = allow_dragging
        self.max_columns = max_columns
        self.draw_overlays = draw_overlays

        # Callbacks
        self._get_item_count: Callable[[], int] = lambda: 0
        self._draw_item: Callable[[pg.Surface, Rect, int, bool, bool], None] = (
            lambda surface, rect, index, selected, hovered: None
        )

        # Hooks
        self.on_select: Optional[Callable[[int], None]] = None
        self.on_open: Optional[Callable[[int], None]] = None

        self._dbl = _DoubleClickTracker()

    # --- Configuration API -------------------------------------------------
    def set_item_count(self, fn: Callable[[], int]) -> None:
        self._get_item_count = fn

    def set_draw_item(self, fn: Callable[[pg.Surface, Rect, int, bool, bool], None]) -> None:
        self._draw_item = fn

    # --- Layout ------------------------------------------------------------
    def _grid_area(self, rect: Rect) -> Rect:
        return Rect(
            rect.x + self.margin,
            rect.y + self.margin,
            max(0, rect.w - 2 * self.margin),
            max(0, rect.h - 2 * self.margin),
        )

    def _compute_columns(self, area_w: int) -> int:
        cw = self.cell_w
        pad = self.padding
        if area_w <= 0:
            return 1
        # Place as many columns as fit with padding between cells
        cols = max(1, (area_w + pad) // (cw + pad))
        if self.max_columns:
            cols = min(cols, self.max_columns)
        return cols

    def _compute_layout(self, state: PickerPanelState) -> None:
        area = self._grid_area(state.rect)
        cols = self._compute_columns(area.w)
        count = self._get_item_count()
        pad = self.padding

        # Precompute rect per item
        item_rects: List[Rect] = []
        if cols <= 0:
            cols = 1
        rows = (count + cols - 1) // cols
        for idx in range(count):
            r = idx // cols
            c = idx % cols
            x = area.x + c * (self.cell_w + pad)
            y = area.y + r * (self.cell_h + pad) - state.scroll_y
            item_rects.append(Rect(x, y, self.cell_w, self.cell_h))

        state.item_rects = item_rects
        state.content_height = rows * (self.cell_h + pad) - pad if rows > 0 else 0

    # --- Rendering ---------------------------------------------------------
    def render(self, surface: pg.Surface, state: PickerPanelState) -> None:
        if not state.visible:
            return

        # Panel background and border
        if self.draw_panel_bg:
            pg.draw.rect(surface, self.bg_color, state.rect)
            pg.draw.rect(surface, self.border_color, state.rect, 1)

        area = self._grid_area(state.rect)
        if self.grid_bg_color is not None:
            pg.draw.rect(surface, self.grid_bg_color, area)

        # Layout and clip
        self._compute_layout(state)
        old_clip = surface.get_clip()
        surface.set_clip(area)

        count = self._get_item_count()
        hovered = state.hovered_index
        selected = state.selected_index
        for idx in range(count):
            rect = state.item_rects[idx]
            # Skip if outside clip vertically (small perf help)
            if rect.bottom < area.top or rect.top > area.bottom:
                continue
            is_sel = selected == idx
            is_hov = hovered == idx

            # Delegate actual item drawing
            self._draw_item(surface, rect, idx, is_sel, is_hov)

            # Overlays
            if self.draw_overlays:
                if is_hov:
                    pg.draw.rect(surface, self.hover_color, rect, 2)
                if is_sel:
                    pg.draw.rect(surface, self.select_color, rect, 3)

        surface.set_clip(old_clip)

        # Scrollbar (simple)
        if state.content_height > area.h:
            self._draw_scrollbar(surface, area, state)

    def _draw_scrollbar(self, surface: pg.Surface, area: Rect, state: PickerPanelState) -> None:
        # Minimal vertical scrollbar on the right edge of the area
        bar_w = 6
        track = Rect(area.right - bar_w, area.top, bar_w, area.h)
        pg.draw.rect(surface, (40, 40, 40), track)
        # Thumb size proportional to view/content
        view_h = area.h
        content_h = state.content_height
        if content_h <= 0:
            return
        ratio = max(0.08, min(1.0, view_h / content_h))
        thumb_h = max(12, int(view_h * ratio))
        max_scroll = max(1, content_h - view_h)
        t = min(1.0, max(0.0, state.scroll_y / max_scroll))
        thumb_y = area.y + int((view_h - thumb_h) * t)
        thumb = Rect(track.x, thumb_y, bar_w, thumb_h)
        pg.draw.rect(surface, (120, 120, 120), thumb)

    # --- Events ------------------------------------------------------------
    def handle_event(self, event: pg.event.Event, state: PickerPanelState) -> None:
        if not state.visible:
            return

        if event.type == pg.MOUSEMOTION:
            self._on_mouse_motion(event, state)
        elif event.type == pg.MOUSEBUTTONDOWN:
            self._on_mouse_down(event, state)
        elif event.type == pg.MOUSEBUTTONUP:
            self._on_mouse_up(event, state)
        elif event.type == pg.MOUSEWHEEL:
            self._on_mouse_wheel(event, state)
        elif event.type == pg.KEYDOWN:
            self._on_key_down(event, state)

    def _panel_contains(self, state: PickerPanelState, pos: Tuple[int, int]) -> bool:
        return state.rect.collidepoint(pos)

    def _on_mouse_motion(self, event: pg.event.Event, state: PickerPanelState) -> None:
        mx, my = event.pos
        if state.dragging:
            dx = event.rel[0]
            dy = event.rel[1]
            state.rect.move_ip(dx, dy)
            return

        if not self._panel_contains(state, (mx, my)):
            state.hovered_index = None
            return

        # Hover detection
        self._compute_layout(state)
        for idx, r in enumerate(state.item_rects):
            if r.collidepoint(mx, my):
                state.hovered_index = idx
                break
        else:
            state.hovered_index = None

    def _on_mouse_down(self, event: pg.event.Event, state: PickerPanelState) -> None:
        if event.button == 3 and self.allow_dragging:  # RMB drag anywhere inside the panel
            if self._panel_contains(state, event.pos):
                state.dragging = True
                state.drag_offset = (event.pos[0] - state.rect.x, event.pos[1] - state.rect.y)
            return

        if event.button == 1:
            if not self._panel_contains(state, event.pos):
                return
            # Select item under cursor
            self._compute_layout(state)
            clicked_index = None
            for idx, r in enumerate(state.item_rects):
                if r.collidepoint(event.pos):
                    clicked_index = idx
                    break
            if clicked_index is not None:
                state.selected_index = clicked_index
                if self.on_select:
                    self.on_select(clicked_index)
                if self._dbl.is_double_click(clicked_index) and self.on_open:
                    self.on_open(clicked_index)

    def _on_mouse_up(self, event: pg.event.Event, state: PickerPanelState) -> None:
        if event.button == 3 and self.allow_dragging:
            state.dragging = False

    def _on_mouse_wheel(self, event: pg.event.Event, state: PickerPanelState) -> None:
        area = self._grid_area(state.rect)
        # Only scroll when mouse over panel
        mouse_pos = pg.mouse.get_pos()
        if not area.collidepoint(mouse_pos):
            return
        self._compute_layout(state)
        if state.content_height <= area.h:
            state.scroll_y = 0
            return
        state.scroll_y = max(0, min(state.scroll_y - event.y * (self.cell_h // 2), state.content_height - area.h))

    def _on_key_down(self, event: pg.event.Event, state: PickerPanelState) -> None:
        if state.selected_index is None:
            return
        count = self._get_item_count()
        if count <= 0:
            return
        area = self._grid_area(state.rect)
        cols = max(1, self._compute_columns(area.w))
        idx = state.selected_index
        if event.key == pg.K_LEFT:
            idx = max(0, idx - 1)
        elif event.key == pg.K_RIGHT:
            idx = min(count - 1, idx + 1)
        elif event.key == pg.K_UP:
            idx = max(0, idx - cols)
        elif event.key == pg.K_DOWN:
            idx = min(count - 1, idx + cols)
        elif event.key == pg.K_RETURN:
            if self.on_open and 0 <= idx < count:
                self.on_open(idx)
            return
        else:
            return

        state.selected_index = idx
        if self.on_select:
            self.on_select(idx)
