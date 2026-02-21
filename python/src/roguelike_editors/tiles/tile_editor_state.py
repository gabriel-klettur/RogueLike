from roguelike_editors.tiles.tiles_picker_panel.tile_picker_state import TilePickerState
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_state import TileToolbarState
from roguelike_editors.tiles.tiles_view_panel.tiles_view_state import TilesViewPanelState
from roguelike_editors.tiles.tiles_title.tiles_tiles_states import TilesTitleState
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_states import TilesCollisionPanelState
from roguelike_editors.tiles.layers_panel.layers_panel_states import LayersPanelState
from roguelike_editors.tiles.size_panel.size_panel_state import SizePanelState
from roguelike_editors.tiles.common.state import deep_copy_state

class TileEditorState:
    """
    Estructura mínima para el modo de edición de tiles.
    """
    def __init__(self):
        self.active = False            # bandera global (F8)
        self.selected_tile = None      # instancia de Tile bajo el cursor        
        self.current_choice = None     # ruta elegida en la paleta
        self.scroll_offset = 0         # desplazamiento de scroll en la paleta
        
        self.current_tool = "select"   # "select" | "brush" | "eyedropper" | "view"
        self.brush_dragging = False    # para arrastrar el brush
        self.default_dragging = False  # para arrastrar la herramienta default
        self.delete_dragging = False   # para arrastrar la herramienta delete
        self.current_layer = Layer.Ground   # capa activa del editor
        
        self.toolbar_state = TileToolbarState()        
        self.picker_state = TilePickerState()        
        self.view_panel_state = TilesViewPanelState()
        self.title_state = TilesTitleState()
        self.collision_panel_state = TilesCollisionPanelState()
        self.layers_panel_state = LayersPanelState()
        self.size_panel_state = SizePanelState()
        # Timestamp para flash de eyedropper
        self.eyedropper_flash_start = None

    def clone(self):
        """
        Return a deep copy of this state instance.
        """
        return deep_copy_state(self)