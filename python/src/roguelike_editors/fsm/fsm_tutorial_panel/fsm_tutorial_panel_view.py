"""
Vista del panel de Tutorial (FSM Editor).
"""
from __future__ import annotations
import pygame
from roguelike_editors.common.ui.panels import draw_translucent_panel
from roguelike_ui.ui_blocker import register_blocker


class FsmTutorialPanelView:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Para resaltar botones de la toolbar principal
        self.main_toolbar_view = None  # se llenará desde el editor
        # Config visual
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
        try:
            self.title_font = pygame.font.SysFont('arial', 22, bold=True)
            self.text_font = pygame.font.SysFont('arial', 18)
            self.button_font = pygame.font.SysFont('arial', 18, bold=True)
        except Exception:
            self.title_font = pygame.font.Font(None, 22)
            self.text_font = pygame.font.Font(None, 18)
            self.button_font = pygame.font.Font(None, 18)

    def _wrap_text(self, text: str, font: pygame.font.Font, max_width: int) -> list[str]:
        words = text.split(' ')
        lines, cur = [], ''
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
        ty = self.padding
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        ty += title_surf.get_height() + self.spacing
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        for line in wrapped:
            ty += self.text_font.size(line)[1] + 2
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box_size = 16
            for item in checklist:
                text_h = self.text_font.size(item.get('label', ''))[1]
                ty += max(box_size, text_h) + 6
        ty += self.padding + 32  # botones
        ty += self.padding
        return max(int(ty), self.min_height)

    def _compute_position(self, screen: pygame.Surface, required_h: int) -> tuple[int, int]:
        margin = 16
        sw, sh = screen.get_size()
        # Anclar por defecto a la derecha de la toolbar principal si existe
        x = sw - self.width - margin
        y = margin + 60
        # evitar salir por abajo
        y = max(margin, min(y, sh - required_h - margin))
        return (x, y)

    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'active', False):
            return
        steps = getattr(self.model, 'steps', [])
        idx = max(0, min(int(getattr(self.model, 'step_index', 0) or 0), max(0, len(steps) - 1)))
        step = steps[idx] if steps else {"title": "", "text": ""}
        total = max(0, len(steps))
        is_last = (total > 0 and idx == total - 1)
        is_first = (idx == 0)

        required_h = self._measure_required_height(step)
        x, y = self._compute_position(screen, required_h)
        panel_rect = pygame.Rect(x, y, self.width, required_h)
        draw_translucent_panel(screen, panel_rect)
        self.model.panel_rect = panel_rect
        try:
            register_blocker(panel_rect)
        except Exception:
            pass

        # Título y progreso
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        screen.blit(title_surf, (x + self.padding, y + self.padding))
        if total > 0:
            prog = f"{idx+1}/{total}"
            prog_surf = self.text_font.render(prog, True, (220, 220, 220))
            px = x + self.width - self.padding - prog_surf.get_width()
            py = y + self.padding + max(0, (title_surf.get_height() - prog_surf.get_height()) // 2)
            screen.blit(prog_surf, (px, py))

        # Texto
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        ty = y + self.padding + title_surf.get_height() + self.spacing
        for line in wrapped:
            ls = self.text_font.render(line, True, self.text_color)
            screen.blit(ls, (x + self.padding, ty))
            ty += ls.get_height() + 2

        # Checklist
        done_set = set()
        try:
            done_set = self.model.checklist_done_by_step.get(idx, set())
        except Exception:
            done_set = set()
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box_size = 16
            for item in checklist:
                iid = item.get('id')
                label = item.get('label', '')
                done = iid in done_set
                box_rect = pygame.Rect(x + self.padding, ty, box_size, box_size)
                pygame.draw.rect(screen, (200, 200, 200), box_rect, 2, border_radius=3)
                if done:
                    inner = box_rect.inflate(-4, -4)
                    pygame.draw.rect(screen, (60, 180, 75), inner, border_radius=3)
                    pygame.draw.line(screen, (255, 255, 255), (box_rect.left + 3, box_rect.centery), (box_rect.centerx - 1, box_rect.bottom - 3), 3)
                    pygame.draw.line(screen, (255, 255, 255), (box_rect.centerx - 1, box_rect.bottom - 3), (box_rect.right - 3, box_rect.top + 3), 3)
                text_surf = self.text_font.render(label, True, (220, 220, 220))
                screen.blit(text_surf, (box_rect.right + 8, box_rect.top - 1))
                ty += max(box_size, text_surf.get_height()) + 6

        # Botones
        btn_w, btn_h = 90, 32
        gap = 10
        close_rect = pygame.Rect(x + self.width - self.padding - btn_w, y + panel_rect.height - self.padding - btn_h, btn_w, btn_h)
        next_rect = pygame.Rect(close_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)
        prev_rect = pygame.Rect(next_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)
        self.model.button_rects = {'prev': prev_rect, 'next': next_rect, 'close': close_rect}
        mouse = pygame.mouse.get_pos()
        for key, rect in [('prev', prev_rect), ('next', next_rect), ('close', close_rect)]:
            disabled = (key == 'next' and is_last) or (key == 'prev' and is_first)
            hovered = rect.collidepoint(mouse) and not disabled
            bg = self.btn_hover if hovered else (self.btn_disabled_bg if disabled else self.btn_bg)
            fg = self.btn_disabled_fg if disabled else self.btn_fg
            pygame.draw.rect(screen, bg, rect, border_radius=6)
            label = 'Anterior' if key == 'prev' else ('Siguiente' if key == 'next' else 'Cerrar')
            ts = self.button_font.render(label, True, fg)
            screen.blit(ts, (rect.centerx - ts.get_width() // 2, rect.centery - ts.get_height() // 2))

        # Highlights
        try:
            highlights = step.get('highlight', {"kind": "none"})
            if isinstance(highlights, dict):
                highlights = [highlights]
            done_set = self.model.checklist_done_by_step.get(idx, set()) if hasattr(self.model, 'checklist_done_by_step') else set()
            for hl in (highlights or []):
                if not isinstance(hl, dict):
                    continue
                hide_if = set(hl.get('hide_if_done') or [])
                deps = set(hl.get('depends_on_done') or [])
                if hide_if and (hide_if & done_set):
                    continue
                if deps and not deps.issubset(done_set):
                    continue
                kind = hl.get('kind')
                if kind == 'toolbar' and self.main_toolbar_view is not None:
                    icon_rects = getattr(self.main_toolbar_view, 'icon_rects', {}) if hasattr(self.main_toolbar_view, 'icon_rects') else {}
                    r = icon_rects.get(hl.get('item'))
                    if r:
                        self._flash(screen, r.inflate(8, 8))
                elif kind == 'graph_toolbar':
                    gp = getattr(self.controller, 'graph_panel_controller', None)
                    rects = getattr(getattr(gp, 'toolbar', None), 'model', None)
                    rect_map = getattr(rects, 'rects_abs', {}) if rects is not None else {}
                    r = rect_map.get(hl.get('item'))
                    if r:
                        self._flash(screen, r.inflate(8, 8))
                elif kind == 'graph_canvas':
                    gpv = getattr(getattr(self.controller, 'graph_panel_controller', None), 'view', None)
                    r = getattr(gpv, 'canvas_rect', None)
                    if r:
                        self._flash(screen, r.inflate(8, 8))
                elif kind == 'legend_button':
                    gpv = getattr(getattr(self.controller, 'graph_panel_controller', None), 'view', None)
                    r = getattr(gpv, 'legend_button_rect', None)
                    if r:
                        self._flash(screen, r.inflate(6, 6))
        except Exception:
            pass

    def _flash(self, screen: pygame.Surface, rect: pygame.Rect) -> None:
        try:
            ticks = pygame.time.get_ticks()
        except Exception:
            ticks = 0
        flash_on = ((ticks // 350) % 2) == 0
        color = (255, 215, 0)
        if flash_on:
            s = pygame.Surface((rect.w, rect.h), pygame.SRCALPHA)
            s.fill((255, 215, 0, 60))
            screen.blit(s, rect.topleft)
        pygame.draw.rect(screen, color, rect, 4)
        pygame.draw.rect(screen, (255, 255, 255), rect.inflate(-6, -6), 2)
