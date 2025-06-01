import pygame
from roguelike_engine.map.model.layer import Layer

class MapEditorState:
    """
    Estado para el Map Editor.
    """
    def __init__(self):
        self.active = False
        self.selected_zone = None
        self.hidden_zones: set[str] = set()
        self.dragging: str | None = None
        self.drag_offset: tuple[int,int] = (0, 0)
        # Toggle dropdown for zone visibility
        self.layers_view_open = False
        # Toggle tile layer visibility
        self.visible_layers: dict[Layer, bool] = {layer: True for layer in Layer}
        # Toggle building layer visibility
        self.show_buildings: bool = True