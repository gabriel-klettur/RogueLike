import pygame
from typing import Dict, Any, Optional
from .spells_properties_panel_models import SpellsPropertiesPanelModel
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsPropertiesPanelView:
    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.blink_interval = 500
        # Optional anchors set by editor
        self._left_anchor_x: Optional[int] = None
        self._top_anchor_y: Optional[int] = None

    def set_anchor(self, left_x: Optional[int], top_y: Optional[int]) -> None:
        """Set external anchor for the panel top-left position, mirroring Entities layout."""
        self._left_anchor_x = left_x
        self._top_anchor_y = top_y

    def _truncate(self, text: str, max_w: int) -> str:
        if self.font.size(text)[0] <= max_w:
            return text
        s = text.rstrip()
        while s and self.font.size(s + "...")[0] > max_w:
            s = s[:-1]
        return s + "..."

    def draw(self, screen: pygame.Surface, model: SpellsPropertiesPanelModel, spells: Dict[str, Any], active_id: Optional[str], title_rect: Optional[pygame.Rect] = None) -> None:
        if not active_id:
            # Clear geometry
            model.panel_rect = None
            model.property_entries.clear()
            return
        sw, sh = screen.get_size()
        margin = UI_MARGIN
        pad = 10
        font_h = self.font.get_height()

        # Panel placement (right side), align below title
        panel_w = min(520, sw // 3 + 120)
        x = (self._left_anchor_x if self._left_anchor_x is not None else sw - panel_w - margin)
        top_base = title_rect.bottom + UI_MARGIN if title_rect else margin
        y = (self._top_anchor_y if self._top_anchor_y is not None else top_base)

        panel_h = sh - y - margin
        panel_rect = pygame.Rect(x, y, panel_w, panel_h)
        model.panel_rect = panel_rect

        # Background
        surf = pygame.Surface(panel_rect.size, pygame.SRCALPHA)
        surf.fill((0, 0, 0, 200))
        screen.blit(surf, panel_rect.topleft)

        # Tabs
        tabs = model.type_tabs
        tab_h = font_h + 10
        tab_w = max(90, (panel_w - pad * 2) // max(1, len(tabs)) - 6)
        model.type_tab_rects.clear()
        tx = panel_rect.x + pad
        ty = panel_rect.y + pad
        for tab in tabs:
            rect = pygame.Rect(tx, ty, tab_w, tab_h)
            model.type_tab_rects[tab] = rect
            color = (60, 120, 60) if tab == model.active_type_tab else (70, 70, 70)
            pygame.draw.rect(screen, color, rect)
            pygame.draw.rect(screen, (200, 200, 200), rect, 1)
            label = self._truncate(tab.title(), tab_w - 8)
            t_surf = self.font.render(label, True, (240, 240, 240))
            screen.blit(t_surf, (rect.x + (rect.w - t_surf.get_width()) // 2, rect.y + (rect.h - t_surf.get_height()) // 2))
            tx += tab_w + 6

        content_rect = pygame.Rect(panel_rect.x + pad, ty + tab_h + pad, panel_w - pad * 2, panel_h - (tab_h + pad * 3))
        model.content_view_rect = content_rect
        # Clip region for scrolling content
        clip_backup = screen.get_clip()
        screen.set_clip(content_rect)

        if model.active_type_tab == 'properties':
            # Render properties list
            model.property_entries.clear()
            data = spells.get(active_id, {})
            # Title row with id
            lines = [(active_id, True)]
            for k, v in data.items():
                if v is None:
                    continue
                lines.append((f"{k}: {v}", False))

            y_off = content_rect.y - model.scroll_y
            line_h = font_h + 2
            max_text_w = content_rect.w - 2
            for idx, (text, is_title) in enumerate(lines):
                if y_off + line_h < content_rect.y:
                    y_off += line_h
                    continue
                if y_off > content_rect.bottom:
                    break
                color = (255, 255, 0) if is_title else (220, 220, 220)
                disp = self._truncate(text, max_text_w)
                t_surf = self.font.render(disp, True, color)
                screen.blit(t_surf, (content_rect.x, y_off))
                if not is_title and ": " in text:
                    key = text.split(': ', 1)[0]
                    rect = pygame.Rect(content_rect.x, y_off, min(t_surf.get_width(), max_text_w), line_h)
                    model.property_entries.append((rect, key))
                y_off += line_h
            model.content_height = len(lines) * line_h

            # Editing/focus overlay
            if model.editing_property:
                for rect, key in model.property_entries:
                    if key == model.editing_property:
                        er = rect.inflate(4, 0)
                        pygame.draw.rect(screen, (128, 0, 128), er, 2)
                        t = pygame.time.get_ticks()
                        if (t % self.blink_interval) < (self.blink_interval // 2):
                            pre = f"{key}: "
                            caret_x = er.x + self.font.size(pre + model.editing_text[:model.editing_cursor])[0]
                            pygame.draw.line(screen, (255, 255, 255), (caret_x, er.y), (caret_x, er.y + self.font.get_height()), 2)
                        break
            elif model.focused_property:
                for rect, key in model.property_entries:
                    if key == model.focused_property:
                        pygame.draw.rect(screen, (255, 255, 0), rect.inflate(4, 0), 2)
                        break
        else:
            # Assets tab: draw sprite cell and preview
            cell_h = content_rect.w
            cell_rect = pygame.Rect(content_rect.x, content_rect.y, content_rect.w, min(cell_h, content_rect.h))
            pygame.draw.rect(screen, (60, 60, 60), cell_rect)
            pygame.draw.rect(screen, (120, 120, 120), cell_rect, 1)
            model.asset_cell_rect = cell_rect
            # Preview
            sprite = None
            try:
                # The editor passes an assets dict keyed by id
                from pygame.transform import smoothscale
                # fetch via external assets (editor will refresh on change)
                # Note: we can't import editor here; we just rely on caller to pass active_id's sprite drawn in picker
                pass
            except Exception:
                pass

        # Restore clip
        screen.set_clip(clip_backup)
