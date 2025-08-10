from typing import Optional, Tuple  # noqa: F401


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
        # Estado auxiliar: si ya aplicamos "default" desde la última activación
        self.default_applied_since_activation: bool = False