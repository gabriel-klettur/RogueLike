import pygame
from roguelike_engine.map.model.layer import Layer


class MapEditorState:
    """
    Estado para el Map Editor, organizado en secciones lógicas:
      1. General (activo, selección, zonas ocultas)
      2. Modos de interacción (añadir, borrar, pintar, etc.)
      3. Diálogos de confirmación
      4. Renombrado de zona
      5. Visibilidad de capas y colisiones
      6. Detección de clics y panning
      7. Rectángulos de botones de toolbar
      8. Ejecución asíncrona de herramientas
    """

    def __init__(self):
        # 1. GENERAL
        self.active: bool = False
        self.selected_zone: str | None = None
        self.hidden_zones: set[str] = set()
        # Persistencia de cámara entre toggles del editor
        #   - saved_game_camera: (offset_x, offset_y, zoom) fuera del editor
        #   - saved_editor_camera: (offset_x, offset_y, zoom) dentro del editor
        self.saved_game_camera = None
        self.saved_editor_camera = None
        # Contador para diferir el follow automático de la cámara tras salir del editor
        # Evita que el update de juego sobreescriba inmediatamente el estado restaurado
        self.defer_follow_frames: int = 0

        # 2. MODOS DE INTERACCIÓN
        self.add_zone_mode: bool = False       # Modo: añadir zona
        self.delete_zone_mode: bool = False    # Modo: borrar zona
        self.paint_tiles_mode: bool = False    # Modo: pintar tiles
        self.clear_colliders_mode: bool = False  # Modo: vaciar colliders
        self.paint_colliders_mode: bool = False  # Modo: pintar colliders
        self.layers_view_open: bool = False    # Modo: dropdown de visibilidad de capas
        # Código de overlay activo para pintar tiles (por defecto, 'floor')
        self.tile_code: str | None = "floor"

        # 3. DIÁLOGOS DE CONFIRMACIÓN
        # -- Borrar zona
        self.confirm_delete_zone: bool = False       # Flag: diálogo activo
        self.pending_delete_zone: str | None = None  # Zona a borrar pendiente de confirmación
        self.confirm_yes_rect: pygame.Rect | None = None  # Rect "Sí" borrar
        self.confirm_no_rect: pygame.Rect | None = None   # Rect "No" borrar

        # -- Pintar tiles
        self.confirm_paint_tiles: bool = False           # Flag: diálogo activo
        self.pending_paint_tiles_zone: str | None = None  # Zona a pintar pendiente
        self.confirm_paint_yes_rect: pygame.Rect | None = None
        self.confirm_paint_no_rect: pygame.Rect | None = None

        # -- Vaciar colliders
        self.confirm_clear_colliders: bool = False
        self.pending_clear_colliders_zone: str | None = None
        self.confirm_clear_colliders_yes_rect: pygame.Rect | None = None
        self.confirm_clear_colliders_no_rect: pygame.Rect | None = None

        # -- Pintar colliders
        self.confirm_paint_colliders: bool = False
        self.pending_paint_colliders_zone: str | None = None
        self.confirm_paint_colliders_yes_rect: pygame.Rect | None = None
        self.confirm_paint_colliders_no_rect: pygame.Rect | None = None

        # -- Añadir zona
        self.confirm_add_zone: bool = False
        self.pending_add_zone_coords: tuple[int, int] | None = None  # (tx, ty) pendiente
        self.confirm_add_yes_rect: pygame.Rect | None = None
        self.confirm_add_no_rect: pygame.Rect | None = None

        # 4. RENOMBRADO DE ZONA
        self.renaming_zone: str | None = None  # Zona que se está renombrando
        self.rename_input: str = ""            # Buffer de texto para renombrar
        self.rename_input_rect: pygame.Rect | None = None  # Rect de la caja de texto
        self.rename_accept_rect: pygame.Rect | None = None  # Rect del botón "Aceptar"

        # 5. VISIBILIDAD DE CAPAS Y COLISIONES
        self.visible_layers: dict[Layer, bool] = {layer: True for layer in Layer}
        self.show_buildings: bool = True
        self.show_colliders: bool = False

        # 6. CLICS Y PANNING
        # -- Detección manual de doble-clic
        self.last_click_zone: str | None = None
        self.last_click_time: int = 0  # milisegundos

        # -- Panning (arrastre con botón medio)
        self.panning: bool = False
        self.pan_start_mouse: tuple[int, int] = (0, 0)
        self.pan_start_offset: tuple[float, float] = (0.0, 0.0)

        # -- Dragging genérico (no usado actualmente)
        self.dragging: str | None = None
        self.drag_offset: tuple[int, int] = (0, 0)

        # 8. EJECUCIÓN ASÍNCRONA DE HERRAMIENTAS
        self.executing_tool: str | None = None   # Nombre de herramienta en ejecución
        self.executing_zone: str | None = None   # Zona objetivo (si aplica)
        self.execution_list: list = []           # Items a procesar (tiles, colliders, etc.)
        self.execution_index: int = 0            # Índice de progreso
        self.execution_total: int = 0            # Total de items
        self.execution_start_time: int = 0       # Tick al iniciar ejecución
        # Último porcentaje de progreso reportado (para logs)
        self.last_progress_report: int = -1
        # Celdas sucias para refresco incremental de chunks (tuplas (ty, tx))
        self.dirty_cells: set[tuple[int, int]] = set()
        # Pila de comandos para Undo/Redo de operaciones (p. ej., pintar tiles)
        self.undo_stack: list = []
        self.redo_stack: list = []
        # Comando actual en construcción durante la ejecución asíncrona
        self.current_command = None
        # Controles de progreso: pausa y botones de UI
        self.execution_paused: bool = False
        self.progress_pause_rect: pygame.Rect | None = None
        self.progress_cancel_rect: pygame.Rect | None = None
        # Marca temporal del último tick en que avanzó la herramienta (para evitar doble avance por frame)
        self.execution_last_tick_ms: int = -1
        # Lista de celdas recientemente pintadas para dibujar overlay en vivo por un corto periodo
        # Formato: list[tuple[int ty, int tx, str code, int expire_ms]]
        self.recent_overlays: list[tuple[int, int, str, int]] = []
        # Anclajes temporales de overlay para detectar y corregir derivas (drift)
        # Mapa (row, col) -> (expected_code, expire_ms)
        self.overlay_locks: dict[tuple[int, int], tuple[str, int]] = {}
        # Cursor para procesar overlay_locks en trozos por frame (rendimiento)
        self.overlay_locks_cursor: int = 0

    # -------------------------------------------------------------
    # MÉTODOS AUXILIARES PARA MANTENER CONSISTENCIA INTERNA
    # -------------------------------------------------------------
    def reset_delete_dialog(self) -> None:
        """Cancela el diálogo de borrar zona."""
        self.confirm_delete_zone = False
        self.pending_delete_zone = None
        self.confirm_yes_rect = None
        self.confirm_no_rect = None

    def reset_paint_tiles_dialog(self) -> None:
        """Cancela el diálogo de pintar tiles."""
        self.confirm_paint_tiles = False
        self.pending_paint_tiles_zone = None
        self.confirm_paint_yes_rect = None
        self.confirm_paint_no_rect = None

    def reset_clear_colliders_dialog(self) -> None:
        """Cancela el diálogo de vaciar colliders."""
        self.confirm_clear_colliders = False
        self.pending_clear_colliders_zone = None
        self.confirm_clear_colliders_yes_rect = None
        self.confirm_clear_colliders_no_rect = None

    def reset_paint_colliders_dialog(self) -> None:
        """Cancela el diálogo de pintar colliders."""
        self.confirm_paint_colliders = False
        self.pending_paint_colliders_zone = None
        self.confirm_paint_colliders_yes_rect = None
        self.confirm_paint_colliders_no_rect = None

    def reset_add_zone_dialog(self) -> None:
        """Cancela el diálogo de agregar zona."""
        self.confirm_add_zone = False
        self.pending_add_zone_coords = None
        self.confirm_add_yes_rect = None
        self.confirm_add_no_rect = None

    def reset_all_dialogs(self) -> None:
        """Cancela todos los diálogos de confirmación activos."""
        self.reset_delete_dialog()
        self.reset_paint_tiles_dialog()
        self.reset_clear_colliders_dialog()
        self.reset_paint_colliders_dialog()
        self.reset_add_zone_dialog()

    def enter_renaming(self, zone_name: str) -> None:
        """
        Inicia el estado de renombrado para 'zone_name',
        prepara el buffer de texto y habilita repetición de teclas.
        """
        self.renaming_zone = zone_name
        self.rename_input = zone_name
        pygame.key.set_repeat(400, 50)

    def exit_renaming(self) -> None:
        """
        Finaliza el estado de renombrado, limpia buffers y rects.
        """
        self.renaming_zone = None
        self.rename_input = ""
        self.rename_input_rect = None
        self.rename_accept_rect = None
        pygame.key.set_repeat()

    def start_panning(self, mouse_pos: tuple[int, int], camera_offset: tuple[float, float]) -> None:
        """
        Activa modo panning:
          - mouse_pos: posición inicial del ratón (pantalla)
          - camera_offset: offset actual de la cámara
        """
        self.panning = True
        self.pan_start_mouse = mouse_pos
        self.pan_start_offset = camera_offset

    def stop_panning(self) -> None:
        """Desactiva modo panning."""
        self.panning = False

    def begin_async_tool(self, tool_name: str, zone: str, items: list) -> None:
        """
        Configura la ejecución asíncrona:
          - tool_name: nombre de la herramienta (ej. "paint_tiles")
          - zone: zona objetivo
          - items: lista de tiles o colliders a procesar
        """
        self.executing_tool = tool_name
        self.executing_zone = zone
        self.execution_list = list(items)
        self.execution_total = len(items)
        self.execution_index = 0
        self.execution_start_time = pygame.time.get_ticks()
        # Reiniciar marcador de progreso para logging controlado
        self.last_progress_report = -1
        # Reiniciar buffer de celdas sucias
        self.dirty_cells.clear()

    def update_async_progress(self) -> None:
        """
        Avanza el índice de progreso; si se completa, limpia el estado asíncrono.
        """
        if self.execution_index < self.execution_total:
            self.execution_index += 1
        else:
            # Al terminar, reiniciar todo
            self.executing_tool = None
            self.executing_zone = None
            self.execution_list.clear()
            self.execution_index = 0
            self.execution_total = 0
            self.execution_start_time = 0