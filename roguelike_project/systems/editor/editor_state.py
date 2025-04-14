# roguelike_project/editor/editor_state.py

class EditorState:
    def __init__(self):
        self.active = False                 # ¿Modo editor activado?
        self.selected_building = None      # Edificio actualmente seleccionado
        self.dragging = False              # ¿Se está arrastrando el edificio?
        self.offset_x = 0                  # Diferencia entre click y posición del edificio (para arrastrar suavemente)
        self.offset_y = 0