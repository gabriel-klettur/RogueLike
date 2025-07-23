import pygame
from typing import Optional, Tuple


from roguelike_engine.map.model.layer import Layer

class TileToolbarState:
    """
    Estado de la barra de herramientas de tiles (UI).
    """
    def __init__(self):
        self.view_active = True                   # para ver los tiles
        # Visible capas y dropdown
        self.layers_view_open = False             # toggle layer visibility dropdown
        self.visible_layers = {layer: True for layer in Layer}
        # Toggle edificios
        self.show_buildings = True
        # Modo colisiones
        self.show_collisions = False
        self.show_collisions_overlay = False
        # Estado del collision picker
        self.collision_picker_open = False
        self.collision_choice = None
        self.collision_picker_rects = {}
        self.collision_picker_pos = None
        self.collision_picker_dragging = False
        self.collision_picker_drag_offset = (0, 0)
        self.collision_picker_panel_size = (0, 0)
        # Toolbar drag state
        self.pos: Optional[Tuple[int, int]] = None
        self.dragging: bool = False
        self.drag_offset: Tuple[int, int] = (0, 0)
        # Rects para botones Delete y Default
        self.btn_delete_rect: Optional[pygame.Rect] = None
        self.btn_default_rect: Optional[pygame.Rect] = None
        
        