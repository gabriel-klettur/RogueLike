# Path: src/roguelike_game/systems/editor/tiles/model/tile_editor_state.py
from roguelike_game.systems.editor.tiles.model.tools.tile_picker_state import TilePickerState
from roguelike_engine.map.model.layer import Layer
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
        self.view_active = True        # para ver los tiles.
        self.current_layer = Layer.Ground   # capa activa del editor
        
        # Layers view tool state
        self.layers_view_open = False      # toggle layer visibility dropdown
        # Visibility state for each layer
        self.visible_layers = {layer: True for layer in Layer}
        # Flag to show/hide buildings in tile editor
        self.show_buildings = True
        
        self.picker_state = TilePickerState()