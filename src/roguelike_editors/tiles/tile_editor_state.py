# Path: src/roguelike_game/systems/editor/tiles/model/tile_editor_state.py
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_state import TilePickerState
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_state import TileToolbarState
class TileEditorState:
    """
    Estructura mínima para el modo de edición de tiles.
    """
    def __init__(self):
        self.active = False            # bandera global (F8)
        self.selected_tile = None      # instancia de Tile bajo el cursor        
        self.current_choice = None     # ruta elegida en la paleta
        self.scroll_offset = 0         # desplazamiento de scroll en la paleta

        # NUEVO: herramientas
        self.current_tool = "select"   # "select" | "brush" | "eyedropper" | "view"
        self.brush_dragging = False    # para arrastrar el brush
        self.current_layer = Layer.Ground   # capa activa del editor

        # Toolbar UI state
        self.toolbar_state = TileToolbarState()

        # Picker state
        self.picker_state = TilePickerState()