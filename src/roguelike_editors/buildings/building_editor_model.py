class BuildingsEditorModel:
    def __init__(self):
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

        # --- Herramienta actual ---
        # Solo 'select' aquí; el panel de colisiones gestiona su propio estado.
        self.current_tool: str = 'select'

        # --- Alcance de edición de colliders ---
        # 'CG' = Cambios Globales (por image_path)
        # 'CU' = Cambios Únicos (sólo instancia activa, no persiste global)
        self.collider_scope: str = 'CG'

        # --- NUEVO: Picker panel draggable ---
        # Si no es None, el panel usa esta posición absoluta en pantalla
        self.picker_manual_pos: tuple[int, int] | None = None
        # Flags/estado de drag del panel
        self.picker_dragging_panel: bool = False
        self.picker_drag_offset: tuple[int, int] = (0, 0)

        # --- NUEVO: Flag para indicar si el panel de colisiones está activo ---
        # Usado para ocultar/deshabilitar herramientas visuales cuando se edita colisiones
        self.colliders_mode: bool = False