import pygame
from typing import Any, Dict, Optional


class ItemsPropertiesPanelView:
    """Vista que renderiza el panel de propiedades de un ítem activo."""

    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.blink_interval = 500

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
        if not active_item_id or active_item_id not in items:
            model.panel_rect = None
            model.property_entries = []
            return

        margin = 20
        sw, sh = screen.get_size()
        top_y = max(margin, (title_rect.bottom + 10) if title_rect else margin)

        item = items[active_item_id]
        raw_name = getattr(item, 'name', str(active_item_id))
        desc = getattr(item, 'description', "")

        # Construir líneas: nombre + descripción + props
        desc_lines = self._wrap_text(desc, sw - margin*4)
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
        max_text_w = max(self.font.size(line)[0] for line in lines) if lines else 0
        panel_w = min(max_text_w + panel_padding*2, sw - margin*2, 500)
        panel_h = min(len(lines)*(font_h + 2) + panel_padding*2, sh - margin*2)
        panel_x = sw - panel_w - margin
        panel_y = top_y

        # Fondo del panel
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (panel_x, panel_y))

        # Actualizar estado de colisiones
        model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        model.property_entries = []
        truncated_entries = []

        # Dibujo de líneas
        tx = panel_x + panel_padding
        ty = panel_y + panel_padding
        for idx_line, line in enumerate(lines):
            color = (255, 255, 0) if idx_line == 0 else (200, 200, 200)
            max_line_width = panel_w - panel_padding*2
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
            screen.blit(txt_surf, (tx, ty))
            # Registrar áreas editables
            if idx_line > desc_count:
                rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                model.property_entries.append((rect, key))
            # Tooltips
            if display_text != text_content:
                rect_tt = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                truncated_entries.append((rect_tt, text_content))
            ty += font_h + 2

        # Guardar para tooltips
        self._truncated_entries = truncated_entries

        # Dibujar decoraciones de edición/enfoque
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

        # Tooltips (post)
        mx, my = pygame.mouse.get_pos()
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
