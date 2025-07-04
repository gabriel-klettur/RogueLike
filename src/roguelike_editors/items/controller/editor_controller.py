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
            elif self.model.visible:
                if event.key == pygame.K_UP:
                    self.model.scroll_index = max(0, self.model.scroll_index - 1)
                elif event.key == pygame.K_DOWN:
                    self.model.scroll_index = min(len(self.model.items) - 1, self.model.scroll_index + 1)
        elif event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            margin = 20
            columns = 6
            cell_size = 64
            text_margin = 4
            text_height = self.view.font.get_height()
            cell_height = cell_size + text_margin + text_height
            if mx < margin or my < margin:
                self.model.hovered_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin) // cell_height
                idx = row * columns + col
                item_ids = list(self.model.items.keys())
                x0 = margin + col * (cell_size + margin)
                y0 = margin + row * cell_height
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
