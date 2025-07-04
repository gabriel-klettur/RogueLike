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
        if model.hovered_item_id and model.hovered_item_id in model.items:
            idx_h = item_ids.index(model.hovered_item_id)
            col = idx_h % columns
            row = idx_h // columns
            if scroll <= row < scroll + visible_rows:
                x = margin + col * (cell_size + margin)
                y = margin + (row - scroll) * (cell_height + margin)
                # Resaltar ítem
                highlight_rect = pygame.Rect(x-2, y-2, cell_size+4, cell_size+4)
                pygame.draw.rect(screen, (255, 255, 0), highlight_rect, 3)
                # Panel de detalles
                info_w = 200
                info_x = sw - info_w - margin
                info_y = margin
                info_h = sh - 2*margin
                info_surf = pygame.Surface((info_w, info_h), pygame.SRCALPHA)
                info_surf.fill((0, 0, 0, 200))
                screen.blit(info_surf, (info_x, info_y))
                detail_x = info_x + 10
                detail_y = info_y + 10
                # Nombre (truncado)
                raw_name = model.items[model.hovered_item_id].name
                name = self._truncate_text(raw_name, info_w - 20)
                name_txt = self.font.render(name, True, (255, 255, 0))
                screen.blit(name_txt, (detail_x, detail_y))
                detail_y += font_h + 5
                # Descripción con wrap
                desc = model.items[model.hovered_item_id].description
                for line in self._wrap_text(desc, info_w - 20):
                    txt = self.font.render(line, True, (200, 200, 200))
                    screen.blit(txt, (detail_x, detail_y))
                    detail_y += font_h + 2
        # Parámetros de layout
        margin = 20
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        sw, sh = screen.get_size()
        columns = max(1, (sw - margin) // (cell_size + margin))
        # Dibujar grid de ítems
        item_ids = list(model.items.keys())
        for idx, item_id in enumerate(item_ids):
            item = model.items[item_id]
            col = idx % columns
            row = idx // columns
            x = margin + col * (cell_size + margin)
            y = margin + row * (cell_height + margin) - model.scroll_index * (cell_height + margin)
            # Saltar filas fuera del área visible
            if y + cell_size < margin or y > sh - margin:
                continue
            # Celda fondo
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            # Icono escalado
            icon = self.assets.get(item_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))

        # Resaltar hover
        # Barra de scroll: sólo dibujar filas visibles
        if model.hovered_item_id and model.hovered_item_id in model.items:
            idx = item_ids.index(model.hovered_item_id)
            col = idx % columns
            row = idx // columns
            x = margin + col * (cell_size + margin)
            y = margin + row * (cell_height + margin) - model.scroll_index * (cell_height + margin)
            highlight_rect = pygame.Rect(x - 2, y - 2, cell_size + 4, cell_size + 4)
            pygame.draw.rect(screen, (255, 255, 0), highlight_rect, 3)
            # Panel de información
            info_w = 200
            info_h = 120
            info_x = screen.get_width() - info_w - margin
            info_y = margin
            info_surf = pygame.Surface((info_w, info_h), pygame.SRCALPHA)
            info_surf.fill((0, 0, 0, 200))
            screen.blit(info_surf, (info_x, info_y))
            # Detalles
            detail_x = info_x + 10
            detail_y = info_y + 10
            # Nombre del ítem
            raw_name = model.items[model.hovered_item_id].name
            name = self._truncate_text(raw_name, info_w - 20)
            name_txt = self.font.render(name, True, (255, 255, 0))
            screen.blit(name_txt, (detail_x, detail_y))
            detail_y += font_h + 5
            # Descripción
            desc = model.items[model.hovered_item_id].description
            lines = self._wrap_text(desc, info_w - 20)
            for line in lines:
                txt = self.font.render(line, True, (200, 200, 200))
                screen.blit(txt, (detail_x, detail_y))
                detail_y += font_h + 2
