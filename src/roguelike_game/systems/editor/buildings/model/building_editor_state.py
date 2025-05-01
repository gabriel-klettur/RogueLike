
# Path: src/roguelike_game/systems/editor/buildings/model/building_editor_state.py
class BuildingsEditorState:
    def __init__(self):
        self.active = False
        self.mode = None  # 🔄 "builder", "entities", etc.

        self.selected_building = None
        self.dragging = False
        self.offset_x = 0
        self.offset_y = 0

        # 🆕 Estados para redimensionamiento
        self.resizing = False
        self.resize_origin = (0, 0)       # posición del mouse al comenzar el resize
        self.initial_size = (0, 0)        # tamaño inicial de la imagens

        # barra slit
        self.split_dragging = False
