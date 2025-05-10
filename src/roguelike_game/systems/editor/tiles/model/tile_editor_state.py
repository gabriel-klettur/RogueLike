# Path: src/roguelike_game/systems/editor/tiles/model/tile_editor_state.py
class TileEditorState:
    """
    Estructura mínima para el modo de edición de tiles.
    """
    def __init__(self):
        self.active = False            # bandera global (F8)
        self.selected_tile = None      # instancia de Tile bajo el cursor
        self.picker_open = False       # ¿paleta abierta?
        self.current_choice = None     # ruta elegida en la paleta
        self.scroll_offset = 0         # desplazamiento de scroll en la paleta

        # NUEVO: herramientas
        self.current_tool = "select"   # "select" | "brush" | "eyedropper" | "view"
        self.brush_dragging = False    # para arrastrar el brush
        self.view_active = True        # para ver los tiles.


        
        
        
