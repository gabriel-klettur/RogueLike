# Path: src/roguelike_game/systems/editor/tiles/model/tile_picker_state.py

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
        self.btn_delete_rect: Optional[pygame.Rect]  = None
        self.btn_default_rect: Optional[pygame.Rect] = None        
