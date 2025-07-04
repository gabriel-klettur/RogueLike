import pygame
from typing import Any, Dict

class ItemEditorView:
    """Renderiza UI del editor de ítems: panel e ítems."""
    def __init__(self, assets: Dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font

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
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    def draw(self, screen: pygame.Surface, model: Any) -> None:
        # Fondo semi-transparente
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        screen.blit(overlay, (0, 0))

        # Parámetros de layout
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        # Columnas según ancho disponible (deja espacio panel)
        columns = max(1, (sw - margin*3) // (cell_size + margin))
        visible_rows = max(1, (sh - 2*margin) // (cell_height + margin))
        item_ids = list(model.items.keys())
        total_rows = (len(item_ids) + columns - 1) // columns
        # Clamp scroll index
        scroll = max(0, min(model.scroll_index, total_rows - visible_rows))

        # Dibujar grid de ítems (solo iconos)
        for idx, item_id in enumerate(item_ids):
            col = idx % columns
            row = idx // columns
            if row < scroll or row >= scroll + visible_rows:
                continue
            x = margin + col * (cell_size + margin)
            y = margin + (row - scroll) * (cell_height + margin)
            # Celda fondo
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            # Icono escalado
            icon = self.assets.get(item_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))

        # Highlight y panel de información
        # Mostrar detalles del item seleccionado o en hover
        active_id = model.selected_item_id or model.hovered_item_id
        if active_id and active_id in model.items:
            # Resaltar ítem
            idx_h = item_ids.index(active_id)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + visible_rows:
                x = margin + col * (cell_size + margin)
                y = margin + (row - scroll) * (cell_height + margin)
                highlight_rect = pygame.Rect(x-2, y-2, cell_size+4, cell_size+4)
                pygame.draw.rect(screen, (255, 255, 0), highlight_rect, 3)

                # Preparar líneas de texto
                item = model.items[active_id]
                raw_name = item.name
                desc = item.description
                # Wrap descripción con ancho máximo provisional
                desc_lines = self._wrap_text(desc, sw - margin*4)
                # Contar líneas de descripción para aplicar cursiva
                desc_count = len(desc_lines)
                lines = [raw_name] + desc_lines

                # Propiedades adicionales (cargadas desde el modelo)
                if hasattr(item, 'model_dump'):
                    data = item.model_dump()
                else:
                    try:
                        data = item.dict()
                    except:
                        data = vars(item)
                for key, val in data.items():
                    # Excluir nombre, descripción y valores null
                    if key in ("name", "description") or val is None:
                        continue
                    lines.append(f"{key}: {val}")

                # Calcular dimensiones dinámicas
                max_text_w = max(self.font.size(line)[0] for line in lines)
                panel_padding = 10
                panel_w = min(max_text_w + panel_padding*2, sw - margin*2, 500)  # ancho máximo del panel
                panel_h = min(len(lines)*(font_h + 2) + panel_padding*2, sh - margin*2)

                panel_x = sw - panel_w - margin
                panel_y = margin

                # Dibujar panel
                info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
                info_surf.fill((0, 0, 0, 200))
                screen.blit(info_surf, (panel_x, panel_y))

                # Render texto con truncamiento y tooltips
                tx = panel_x + panel_padding
                ty = panel_y + panel_padding
                truncated_entries = []
                for idx_line, line in enumerate(lines):
                    color = (255, 255, 0) if idx_line == 0 else (200, 200, 200)
                    full_text = line
                    max_line_width = panel_w - panel_padding*2
                    display_text = self._truncate_text(full_text, max_line_width)
                    # Aplicar cursiva a descripción
                    if idx_line > 0 and idx_line <= desc_count:
                        self.font.set_italic(True)
                    txt_surf = self.font.render(display_text, True, color)
                    # Revertir estilo
                    if idx_line > 0 and idx_line <= desc_count:
                        self.font.set_italic(False)
                    screen.blit(txt_surf, (tx, ty))
                    if display_text != full_text:
                        rect = pygame.Rect(tx, ty, txt_surf.get_width(), font_h)
                        truncated_entries.append((rect, full_text))
                    ty += font_h + 2
                # Tooltips on hover
                mx, my = pygame.mouse.get_pos()
                for rect, full_text in truncated_entries:
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
        # Parámetros de layout
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = max(1, (sw - margin) // (cell_size + margin))

