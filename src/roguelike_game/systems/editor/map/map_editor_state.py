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
        self.renaming_zone: str | None = None  # Current zone being renamed
        self.rename_input: str = ""  # Buffer for rename text
        self.rename_input_rect: pygame.Rect | None = None  # Rect para caja de input
        self.rename_accept_rect: pygame.Rect | None = None  # Rect para botón aceptar
        # For manual double-click detection
        self.last_click_zone: str | None = None
        self.last_click_time: int = 0  # milliseconds