import pygame
from typing import Any, Dict, Optional


class ItemsPropertiesPanelView:
    """Vista que renderiza el panel de propiedades de un ítem activo."""

    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.blink_interval = 500
        # Tamaño fijo del panel
        self.panel_w = 420
        self.panel_h = 360

    # Helpers de texto
    def _wrap_text(self, text: str, max_width: int) -> list[str]:
        words = text.split(' ')
        lines: list[str] = []
        current = ''
        for w in words:
            test = current + (' ' if current else '') + w
            if self.font.size(test)[0] <= max_width:
                current = test
            else:
                lines.append(current)
                current = w
        if current:
            lines.append(current)
        return lines

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while text and self.font.size(text + '...')[0] > max_width:
            text = text[:-1]
        return text + '...'

    def draw(self,
             screen: pygame.Surface,
             model,
             items: Dict[str, Any],
             active_item_id: Optional[str],
             title_rect: Optional[pygame.Rect] = None) -> None:
        margin = 20
        sw, sh = screen.get_size()
        if not active_item_id or active_item_id not in items:
            # Sin ítem activo: ocultar completamente el panel
            model.panel_rect = None
            model.property_entries = []
            model.content_height = 0
            model.content_view_rect = None
            return

        top_y = max(margin, (title_rect.bottom + 10) if title_rect else margin)

        item = items[active_item_id]
        raw_name = getattr(item, 'name', str(active_item_id))
        desc = getattr(item, 'description', "")

        # Construir líneas: nombre + descripción + props
        desc_lines = self._wrap_text(desc, max(10, self.panel_w - 40))
        desc_count = len(desc_lines)
        lines: list[str] = [raw_name] + desc_lines
        if hasattr(item, 'model_dump'):
            data = item.model_dump()
        else:
            try:
                data = item.dict()
            except Exception:
                data = vars(item)
        for key, val in data.items():
            if key in ("name", "description") or val is None:
                continue
            lines.append(f"{key}: {val}")

        font_h = self.font.get_height()
        panel_padding = 10
        panel_w = self.panel_w
        # Mantener en pantalla si no cabe completamente
        panel_h = self.panel_h if (top_y + self.panel_h + margin) <= sh else max(80, sh - top_y - margin)
        panel_x = sw - panel_w - margin
        panel_y = top_y

        # Fondo del panel (fijo)
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (panel_x, panel_y))

        # Actualizar estado de colisiones y viewport de contenido
        model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        view_rect = pygame.Rect(panel_x + panel_padding, panel_y + panel_padding, panel_w - 2*panel_padding, panel_h - 2*panel_padding)
        model.content_view_rect = view_rect

        # Preparar dibujo con clipping y scroll
        model.property_entries = []
        truncated_entries = []
        max_line_width = view_rect.w
        model.content_height = len(lines) * (font_h + 2)
        old_clip = screen.get_clip()
        screen.set_clip(view_rect)

        tx = view_rect.x
        ty0 = view_rect.y
        for idx_line, line in enumerate(lines):
            color = (255, 255, 0) if idx_line == 0 else (200, 200, 200)
            if idx_line > desc_count:
                key, val = line.split(': ', 1)
                text_content = (
                    f"{key}: {model.editing_text}" if model.editing_property == key else f"{key}: {val}"
                )
            else:
                text_content = line
            display_text = self._truncate_text(text_content, max_line_width)
            if 0 < idx_line <= desc_count:
                self.font.set_italic(True)
            txt_surf = self.font.render(display_text, True, color)
            if 0 < idx_line <= desc_count:
                self.font.set_italic(False)

            # Y con scroll
            y = ty0 + idx_line * (font_h + 2) - model.scroll_y
            line_rect = pygame.Rect(tx, y, txt_surf.get_width(), font_h)
            # Sólo dibujar si intersecta el viewport
            if line_rect.bottom >= view_rect.top and line_rect.top <= view_rect.bottom:
                screen.blit(txt_surf, (tx, y))
                if idx_line > desc_count:
                    model.property_entries.append((line_rect, key))
                if display_text != text_content:
                    truncated_entries.append((line_rect, text_content))

        screen.set_clip(old_clip)
        self._truncated_entries = truncated_entries

        # Scrollbar si overflow
        if model.content_height > view_rect.h:
            bar_w = 6
            track = pygame.Rect(view_rect.right - bar_w, view_rect.top, bar_w, view_rect.h)
            pygame.draw.rect(screen, (40, 40, 40), track)
            ratio = max(0.08, min(1.0, view_rect.h / max(1, model.content_height)))
            thumb_h = max(12, int(view_rect.h * ratio))
            max_scroll = max(1, model.content_height - view_rect.h)
            t = min(1.0, max(0.0, model.scroll_y / max_scroll))
            thumb_y = view_rect.y + int((view_rect.h - thumb_h) * t)
            thumb = pygame.Rect(track.x, thumb_y, bar_w, thumb_h)
            pygame.draw.rect(screen, (120, 120, 120), thumb)

        # Decoraciones de edición/enfoque (usar rects ya scrolleados)
        if getattr(model, 'editing_property', None):
            for rect_prop, key_prop in getattr(model, 'property_entries', []):
                if key_prop == model.editing_property:
                    ed_rect = rect_prop.inflate(4, 0)
                    pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                    break
        elif getattr(model, 'focused_property', None):
            for rect_prop, key_prop in getattr(model, 'property_entries', []):
                if key_prop == model.focused_property:
                    hl_rect = rect_prop.inflate(4, 0)
                    pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                    break

        # Tooltips (post) sólo si el ratón está sobre el viewport
        mx, my = pygame.mouse.get_pos()
        if model.content_view_rect and model.content_view_rect.collidepoint(mx, my):
            for rect, full_text in getattr(self, '_truncated_entries', []):
                if rect.collidepoint(mx, my):
                    tt_w = self.font.size(full_text)[0] + 8
                    tt_h = font_h + 4
                    tt_x = min(mx + 10, sw - tt_w - margin)
                    tt_y = min(my + 10, sh - tt_h - margin)
                    tooltip_surf = pygame.Surface((tt_w, tt_h), pygame.SRCALPHA)
                    tooltip_surf.fill((0, 0, 0, 220))
                    tooltip_txt = self.font.render(full_text, True, (255, 255, 255))
                    tooltip_surf.blit(tooltip_txt, (4, 2))
                    screen.blit(tooltip_surf, (tt_x, tt_y))
                    break
