import pygame
from typing import Any, Dict
from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_editors.items.view.editor_view import ItemEditorView

class ItemEditorController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemEditorModel(items=items, assets=assets)
        self.view = ItemEditorView(assets, font)

    def handle_event(self, event: pygame.event.Event) -> None:
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F7:
                self.model.visible = not self.model.visible
                if not self.model.visible:
                    self.model.selected_item_id = None
            elif self.model.visible:
                if event.key == pygame.K_UP:
                    self.model.scroll_index = max(0, self.model.scroll_index - 1)
                elif event.key == pygame.K_DOWN:
                    self.model.scroll_index = min(len(self.model.items) - 1, self.model.scroll_index + 1)

        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            screen_surf = pygame.display.get_surface()
            if screen_surf:
                sw, sh = screen_surf.get_size()
            else:
                sw, sh = None, None
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            if sw:
                columns = max(1, (sw - margin) // (cell_size + margin))
            else:
                columns = 6
            # Seleccionar item
            if mx < margin or my < margin:
                self.model.selected_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = list(self.model.items.keys())
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_item_id = item_ids[idx]
                else:
                    self.model.selected_item_id = None
        elif event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            screen_surf = pygame.display.get_surface()
            if screen_surf:
                sw, sh = screen_surf.get_size()
            else:
                sw, sh = None, None
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            if sw:
                columns = max(1, (sw - margin) // (cell_size + margin))
            else:
                columns = 6
            # Verificar área vertical
            if mx < margin or my < margin:
                self.model.hovered_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = list(self.model.items.keys())
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_item_id = item_ids[idx]
                else:
                    self.model.hovered_item_id = None

        else:
            # Reset hover cuando otros eventos
            self.model.hovered_item_id = None

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
