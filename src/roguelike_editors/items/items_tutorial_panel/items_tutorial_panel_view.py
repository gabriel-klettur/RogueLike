from __future__ import annotations

import pygame
from typing import Optional
from roguelike_editors.common.ui.panels import draw_translucent_panel
from roguelike_ui.ui_blocker import register_blocker


class ItemsTutorialPanelView:
    def __init__(self, editor_controller, model) -> None:
        self.editor = editor_controller
        self.model = model
        # Toolbar views injected by integrator
        self.items_toolbar_view = None
        self.add_remove_toolbar_view = None
        # Visual config
        self.width = 520
        self.min_height = 200
        self.padding = 14
        self.spacing = 8
        self.title_color = (255, 255, 0)
        self.text_color = (230, 230, 230)
        self.btn_bg = (60, 60, 60)
        self.btn_fg = (255, 255, 255)
        self.btn_hover = (90, 90, 90)
        self.btn_disabled_bg = (45, 45, 45)
        self.btn_disabled_fg = (150, 150, 150)
        # Fonts
        try:
            self.title_font = pygame.font.SysFont('arial', 22, bold=True)
            self.text_font = pygame.font.SysFont('arial', 18)
            self.button_font = pygame.font.SysFont('arial', 18, bold=True)
        except Exception:
            self.title_font = pygame.font.Font(None, 22)
            self.text_font = pygame.font.Font(None, 18)
            self.button_font = pygame.font.Font(None, 18)

    # --- Layout helpers -----------------------------------------------------
    def _wrap_text(self, text: str, font: pygame.font.Font, max_width: int) -> list[str]:
        words = text.split(' ')
        lines = []
        cur = ''
        for w in words:
            test = (cur + ' ' + w).strip()
            if font.size(test)[0] <= max_width:
                cur = test
            else:
                if cur:
                    lines.append(cur)
                cur = w
        if cur:
            lines.append(cur)
        return lines

    def _measure_required_height(self, step: dict) -> int:
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        ty = self.padding + title_surf.get_height() + self.spacing
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        for line in wrapped:
            ty += self.text_font.size(line)[1] + 2
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box_size = 16
            for it in checklist:
                text_h = self.text_font.size(it.get('label', ''))[1]
                ty += max(box_size, text_h) + 6
        btn_h = 32
        ty += self.padding + btn_h
        ty += self.padding
        return max(int(ty), self.min_height)

    def _compute_position(self, screen: pygame.Surface, required_h: int) -> tuple[int, int]:
        margin = 16
        sw, sh = screen.get_size()
        # Prefer to align under the title of the Items editor
        try:
            title_rect = getattr(self.editor.title_controller.view, 'widget', None)
            if title_rect is not None and hasattr(title_rect, 'rect'):
                tr = title_rect.rect
            else:
                tr = None
        except Exception:
            tr = None
        # If Items sub-toolbar is visible, try to place to the right of it
        try:
            add_rm_visible = bool(getattr(self.editor.items_add_remove_model, 'visible', False))
        except Exception:
            add_rm_visible = False
        if add_rm_visible and self.add_remove_toolbar_view is not None:
            try:
                tbv = self.add_remove_toolbar_view.widget
                pos = tbv.panel.pos or (tbv.x, tbv.y)
                panel_w = tbv.panel.surface.get_width()
                x = min(max(margin, pos[0] + panel_w + 8), max(margin, sw - self.width - margin))
                if tr is not None:
                    y = tr.bottom + 8
                else:
                    y = margin
                y = max(margin, min(y, max(margin, sh - required_h - margin)))
                return (x, y)
            except Exception:
                pass
        # Else, align to the right of the main Items toolbar
        if self.items_toolbar_view is not None:
            try:
                w = self.items_toolbar_view.widget.panel.surface.get_width()
                x = min(max(margin, self.items_toolbar_view.widget.panel.pos[0] + w + 8), max(margin, sw - self.width - margin))
                if tr is not None:
                    y = tr.bottom + 8
                else:
                    y = margin
                y = max(margin, min(y, max(margin, sh - required_h - margin)))
                return (x, y)
            except Exception:
                pass
        # Fallback: right side
        x = max(margin, sw - self.width - margin)
        y = max(margin, 96)
        return (x, y)

    # --- Rendering ----------------------------------------------------------
    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'active', False):
            return
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', []) or []
        idx = max(0, min(idx, max(0, len(steps) - 1)))
        total = len(steps)
        is_first = (idx == 0)
        is_last = (total > 0 and idx == total - 1)
        step = steps[idx] if steps else {"title": "", "text": ""}
        done_set = self.model.checklist_done_by_step.get(idx, set())

        required_h = self._measure_required_height(step)
        x, y = self._compute_position(screen, required_h)
        panel_rect = pygame.Rect(x, y, self.width, required_h)
        draw_translucent_panel(screen, panel_rect)
        self.model.panel_rect = panel_rect
        try:
            register_blocker(panel_rect)
        except Exception:
            pass
        # Title and progress
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        screen.blit(title_surf, (x + self.padding, y + self.padding))
        if total > 0:
            prog = f"{idx+1}/{total}"
            prog_surf = self.text_font.render(prog, True, (220, 220, 220))
            px = x + self.width - self.padding - prog_surf.get_width()
            py = y + self.padding + max(0, (title_surf.get_height() - prog_surf.get_height()) // 2)
            screen.blit(prog_surf, (px, py))
        # Text
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        ty = y + self.padding + title_surf.get_height() + self.spacing
        for line in wrapped:
            surf = self.text_font.render(line, True, self.text_color)
            screen.blit(surf, (x + self.padding, ty))
            ty += surf.get_height() + 2
        # Checklist
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box = 16
            for it in checklist:
                lab = it.get('label', '')
                iid = it.get('id')
                done = (iid in done_set)
                rect = pygame.Rect(x + self.padding, ty, box, box)
                pygame.draw.rect(screen, (200, 200, 200), rect, 2, border_radius=3)
                if done:
                    inner = rect.inflate(-4, -4)
                    pygame.draw.rect(screen, (60, 180, 75), inner, border_radius=3)
                    pygame.draw.line(screen, (255, 255, 255), (rect.left + 3, rect.centery), (rect.centerx - 1, rect.bottom - 3), 3)
                    pygame.draw.line(screen, (255, 255, 255), (rect.centerx - 1, rect.bottom - 3), (rect.right - 3, rect.top + 3), 3)
                txt = self.text_font.render(lab, True, (220, 220, 220))
                screen.blit(txt, (rect.right + 8, rect.top - 1))
                ty += max(box, txt.get_height()) + 6
        # Buttons
        btn_w, btn_h = 90, 32
        gap = 10
        close_rect = pygame.Rect(x + self.width - self.padding - btn_w, y + panel_rect.height - self.padding - btn_h, btn_w, btn_h)
        next_rect = pygame.Rect(close_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)
        prev_rect = pygame.Rect(next_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)
        self.model.button_rects = {'prev': prev_rect, 'next': next_rect, 'close': close_rect}
        mouse = pygame.mouse.get_pos()
        for key, rect in [('prev', prev_rect), ('next', next_rect), ('close', close_rect)]:
            disabled = (key == 'next' and is_last) or (key == 'prev' and is_first)
            hovered = (rect.collidepoint(mouse) and not disabled)
            bg = self.btn_hover if hovered else (self.btn_disabled_bg if disabled else self.btn_bg)
            fg = self.btn_disabled_fg if disabled else self.btn_fg
            pygame.draw.rect(screen, bg, rect, border_radius=6)
            label = 'Anterior' if key == 'prev' else ('Siguiente' if key == 'next' else 'Cerrar')
            txt = self.button_font.render(label, True, fg)
            screen.blit(txt, (rect.centerx - txt.get_width() // 2, rect.centery - txt.get_height() // 2))
        # Highlights
        self._render_highlights(screen, step, done_set)

    def _render_highlights(self, screen: pygame.Surface, step: dict, done_set: set) -> None:
        def _flash_rect(r: pygame.Rect, inflate: int = 8):
            if not r:
                return
            rr = r.inflate(inflate, inflate)
            ticks = pygame.time.get_ticks()
            flash_on = ((ticks // 350) % 2) == 0
            color = (255, 215, 0)
            if flash_on:
                s = pygame.Surface((rr.w, rr.h), pygame.SRCALPHA)
                s.fill((255, 215, 0, 60))
                screen.blit(s, rr.topleft)
            pygame.draw.rect(screen, color, rr, 4)
            pygame.draw.rect(screen, (255, 255, 255), rr.inflate(-6, -6), 2)

        hls = step.get('highlight', {"kind": "none"})
        if isinstance(hls, dict):
            hls = [hls]
        if not isinstance(hls, list):
            return
        for hl in hls:
            if not isinstance(hl, dict):
                continue
            hide_if = set(hl.get('hide_if_done') or [])
            deps = set(hl.get('depends_on_done') or [])
            if hide_if and (hide_if & done_set):
                continue
            if deps and not deps.issubset(done_set):
                continue
            kind = hl.get('kind')
            if kind == 'toolbar' and self.items_toolbar_view is not None and hasattr(self.items_toolbar_view, 'widget'):
                rects = getattr(self.items_toolbar_view.widget, 'icon_rects', {}) or {}
                r = rects.get(hl.get('item'))
                if r:
                    _flash_rect(r, 8)
            elif kind == 'add_remove_toolbar' and self.add_remove_toolbar_view is not None and hasattr(self.add_remove_toolbar_view, 'widget'):
                rects = getattr(self.add_remove_toolbar_view.widget, 'icon_rects', {}) or {}
                r = rects.get(hl.get('item'))
                if r:
                    _flash_rect(r, 8)
            elif kind == 'properties_panel':
                try:
                    r = getattr(getattr(self.editor.properties_controller, 'model', None), 'panel_rect', None)
                    if r:
                        _flash_rect(r, 10)
                except Exception:
                    pass
            elif kind == 'picker_panel':
                try:
                    r = getattr(getattr(self.editor.picker_controller, 'picker_state', None), 'rect', None)
                    if r:
                        _flash_rect(r, 8)
                except Exception:
                    pass
