from typing import Optional, Tuple
import pygame

class TilePickerState:
    """
    Estado puro del TilePicker:
      - open: si la paleta está abierta
      - current_choice: ruta al asset actualmente marcado
      - scroll_offset: desplazamiento vertical de la rejilla
      - pos: esquina superior izquierda de la paleta en pantalla
      - dragging: flag de arrastre de la ventana
      - drag_offset: offset al iniciar el drag
      - surface: pygame.Surface usada para el fondo
      - btn_*_rect: rects de los botones para la View
    """
    def __init__(self):
        self.open: bool = False
        self.current_choice: Optional[str] = None
        self.scroll_offset: int = 0

        # Para mover toda la ventana de la paleta
        self.pos: Optional[Tuple[int, int]] = None
        self.dragging: bool = False
        self.drag_offset: Tuple[int, int] = (0, 0)

        # Surface y botones (la View las rellena)
        self.surface: Optional[pygame.Surface] = None

        # Close button rectangle
        self.btn_close_rect: Optional[pygame.Rect] = None
        # Tileset checkbox state
        self.tileset_filter: bool = False
        # Tileset grid input state
        self.tileset_grid_size_text: str = "32"
        self.tileset_grid_size: int = int(self.tileset_grid_size_text)
        self.tileset_input_active: bool = False
        self.tileset_input_rect: Optional[pygame.Rect] = None
        self.tileset_checkbox_rect: Optional[pygame.Rect] = None
        # Botón crear tiles
        self.btn_tileset_rect: Optional[pygame.Rect]  = None
        # Selected tileset image for slicing via 'Crear tiles'
        self.tileset_source: Optional[str] = None

        # Config button for reordering tiles
        self.btn_config_rect: Optional[pygame.Rect] = None
        self.config_mode: bool = False
        self.config_src_idx: Optional[int] = None