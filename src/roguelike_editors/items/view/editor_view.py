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

    def draw(self, screen: pygame.Surface, model: Any) -> None:
        # Fondo semi-transparente
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        screen.blit(overlay, (0, 0))
        # Parámetros de layout
        margin = 20
        columns = 6
        cell_size = 64
        text_margin = 4
        font_h = self.font.get_height()
        cell_height = cell_size + text_margin + font_h
        # Dibujar grid de ítems
        item_ids = list(model.items.keys())
        for idx, item_id in enumerate(item_ids):
            item = model.items[item_id]
            col = idx % columns
            row = idx // columns
            x = margin + col * (cell_size + margin)
            y = margin + row * (cell_height + margin)
            # Celda fondo
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)
            # Icono escalado
            icon = self.assets.get(item_id)
            if icon:
                icon_surf = pygame.transform.smoothscale(icon, (cell_size, cell_size))
                screen.blit(icon_surf, (x, y))
            # Nombre centrado
            name_surf = self.font.render(item.name, True, (255, 255, 255))
            nx = x + (cell_size - name_surf.get_width()) // 2
            ny = y + cell_size + text_margin
            screen.blit(name_surf, (nx, ny))
        # Resaltar hover
        if model.hovered_item_id and model.hovered_item_id in model.items:
            idx = item_ids.index(model.hovered_item_id)
            col = idx % columns
            row = idx // columns
            x = margin + col * (cell_size + margin)
            y = margin + row * (cell_height + margin)
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
            name_txt = self.font.render(model.items[model.hovered_item_id].name, True, (255, 255, 0))
            screen.blit(name_txt, (detail_x, detail_y))
            detail_y += font_h + 5
            # Descripción
            desc = model.items[model.hovered_item_id].description
            lines = self._wrap_text(desc, info_w - 20)
            for line in lines:
                txt = self.font.render(line, True, (200, 200, 200))
                screen.blit(txt, (detail_x, detail_y))
                detail_y += font_h + 2
