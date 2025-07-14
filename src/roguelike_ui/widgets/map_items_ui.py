import json, os
import pygame
from roguelike_ui.widgets.list_panel_ui import ListPanelUI

class MapItemsUI:
    """
    Widget para listar y seleccionar instancias de ítems en el mapa.
    """
    def __init__(self, font: pygame.font.Font, json_path: str):
        self.font = font
        self.json_path = json_path
        self.data: dict = {}
        self.list_ui = ListPanelUI(font)
        self.selected_instance: str | None = None

    def load(self):
        try:
            with open(self.json_path, 'r') as f:
                self.data = json.load(f)
        except Exception:
            self.data = {}
        display = []
        for inst_id, inst in self.data.items():
            pos = inst.get('position', {})
            display.append(f"{inst_id}: {inst.get('item_id')} @({pos.get('x')},{pos.get('y')})")
        self.list_ui.set_items(display)

    def draw(self, surface: pygame.Surface, rect: pygame.Rect) -> None:
        self.load()
        self.list_ui.draw(surface, rect)

    def handle_event(self, event: pygame.event.Event) -> str | None:
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            idx = self.list_ui.get_selected(event.pos)
            if idx is not None:
                inst_id = list(self.data.keys())[idx]
                self.selected_instance = inst_id
                return inst_id
        return None
