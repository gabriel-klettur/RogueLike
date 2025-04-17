class TileEditorState:
    """
    Estructura mínima para el modo de edición de tiles.
    """
    def __init__(self):
        self.active = False            # bandera global (F8)
        self.selected_tile = None      # instancia de Tile bajo el cursor
        self.picker_open = False       # ¿está visible la paleta?
        self.current_choice = None     # ruta elegida en la paleta
        self.scroll_offset = 0         # desplazamiento de scroll en la paleta