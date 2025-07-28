class BuildingsEditorState:
    def __init__(self):

        self.mode = None #! para edificios?????? buildings????? entities????? parece deprecado

        # Editor principal
        self.active = False

        # Estado normal de edición
        self.selected_building = None
        self.hovered_building = None  # Edificio bajo el cursor
        self.hovered_buildings = []   # Lista de edificios bajo el cursor
        self.hovered_building_index = 0  # Índice en la lista
        self.dragging = False
        self.offset_x = 0
        self.offset_y = 0

        # Edición de tamaño y split
        self.resizing = False
        self.resize_origin = (0, 0)
        self.initial_size = (0, 0)
        self.split_dragging = False

        # --- NUEVO: Picker de edificios ---
        self.picker_active: bool = False
        self.current_dir: str = "assets/buildings"
        self.history: list[str] = []
        self.entries: list = []             # populado por picker_controller
        self.selected_entry = None          # elemento actual (para drag)
        self.dragging_building: bool = False

        # --- NUEVO: Collision brush UI ---
        # Herramienta actual: 'select' | 'collision_brush'
        self.current_tool: str = 'select'
        self.collision_picker_open = False   # toggle del picker de colisión
        self.collision_choice = None         # '#' o '.' seleccionado
        self.collision_picker_rects = {}     # rects de UI del picker
        self.collision_picker_pos = None     # posición del panel picker
        self.collision_picker_dragging = False
        self.collision_picker_drag_offset = (0, 0)
        self.collision_picker_panel_size = (0, 0)
        # Flag to indicate collision brush painting active
        self.collision_brush_dragging: bool = False
# Path: src/roguelike_game/systems/editor/buildings/model/building_editor_state.py