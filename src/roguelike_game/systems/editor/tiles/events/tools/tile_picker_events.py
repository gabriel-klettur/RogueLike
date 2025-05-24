# Path: src/roguelike_game/systems/editor/tiles/events/tools/tile_picker_events.py
from roguelike_game.systems.editor.tiles.tiles_editor_config import PAD, THUMB, COLS

class TilePickerEventHandler:
    """
    Extrae la lógica de handle_click del controller y la sitúa en eventos/tools.
    """
    def __init__(self, picker_controller, editor_state, picker_state):
        self.controller = picker_controller
        self.editor_state = editor_state
        self.picker_state = picker_state        

    def handle_click(self, mouse_pos, button, map):
        """
        Procesa la interacción del picker según posición, botón y estado.
        """
        # Si no está abierto, nada que hacer
        if not self.picker_state.open or self.picker_state.surface is None:
            return False

        # Coordenadas locales al picker
        lx = mouse_pos[0] - (self.picker_state.pos[0] or 0)
        ly = mouse_pos[1] - (self.picker_state.pos[1] or 0)
        sw, sh = self.picker_state.surface.get_size()
        if lx < 0 or ly < 0 or lx > sw or ly > sh:
            return False

        # Botones Borrar / Default
        if self.picker_state.btn_delete_rect and self.picker_state.btn_delete_rect.collidepoint((lx, ly)):
            self.controller._delete_tile(map)
            return True
        if self.picker_state.btn_default_rect and self.picker_state.btn_default_rect.collidepoint((lx, ly)):
            self.controller._set_default(map)
            return True
        # Botón Cerrar
        if self.picker_state.btn_close_rect and self.picker_state.btn_close_rect.collidepoint((lx, ly)):
            self.controller._close()
            return True
        # Arrastrar ventana completa con botón derecho
        if button == 3:
            self.picker_state.dragging = True
            self.picker_state.drag_offset = (lx, ly)
            return True
        # Cálculo de índice en la rejilla de assets
        col = (lx - PAD) // (THUMB + PAD)
        row = (ly - PAD + self.editor_state.scroll_offset) // (THUMB + PAD)
        idx = row * COLS + col

        # Obtener lista actual de assets
        assets = self.controller.assets
        # Validar clic dentro de la rejilla
        if not (0 <= col < COLS and row >= 0 and idx < len(assets)):
            return False

        value, _, is_dir = assets[idx]
        # Navegación de directorios
        if is_dir:
            if value == "..":
                self.controller.current_dir = self.controller.current_dir.parent
            else:
                self.controller.current_dir = self.controller.current_dir / value
            self.controller._load_assets()
            return True

        # Selección de fichero
        self.editor_state.current_choice = value
        self.picker_state.current_choice = value

        return True