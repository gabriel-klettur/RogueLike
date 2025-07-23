"""
Module: roguelike_editors.tiles.tiles_picker_panel.tile_picker_events

Provides TilePickerEventHandler to process user interactions in the tile picker panel,
delgating click, drag, and input events to specialized handlers for toolbar, filters,
dragging, and grid clicks.
"""
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, COLS
import pygame

class TilePickerEventHandler:
    """
    Extrae la lógica de handle_click del controller y la sitúa en eventos/tools.
    """
    def __init__(self, picker_controller, editor_state, picker_state):
        self.controller = picker_controller
        self.editor_state = editor_state
        self.picker_state = picker_state
        # Double-click tracking
        self.last_click_time = 0
        self.last_click_value = None        

    def handle_click(self, mouse_pos, button, map):
        """
        Procesa la interacción del picker según posición, botón y estado.
        """
        if not self.picker_state.open or self.picker_state.surface is None:
            return False

        # Coordenadas locales al picker
        lx = mouse_pos[0] - (self.picker_state.pos[0] or 0)
        ly = mouse_pos[1] - (self.picker_state.pos[1] or 0)
        print(f"[TilePicker] Click at {mouse_pos}, local=({lx},{ly}), dir={self.controller.current_dir}")
        sw, sh = self.picker_state.surface.get_size()
        if lx < 0 or ly < 0 or lx > sw or ly > sh:
            return False

        # Delegar a manejadores específicos
        if self._handle_toolbar_buttons(lx, ly, map):
            return True
        if self._handle_tileset_filter_click(lx, ly):
            return True
        if self._handle_tileset_input_click(lx, ly):
            return True
        if self._handle_tileset_create_click(lx, ly, map):
            return True
        if self._handle_drag_start(button, lx, ly):
            return True
        if self._handle_grid_click(lx, ly, map):
            return True

        return False

    def _handle_toolbar_buttons(self, lx, ly, map):
        """Handle toolbar button interactions: delete tile, set default, and close picker."""
        # Botones Borrar / Default / Cerrar
        if self.picker_state.btn_delete_rect and self.picker_state.btn_delete_rect.collidepoint((lx, ly)):
            self.controller._delete_tile(map)
            return True
        if self.picker_state.btn_default_rect and self.picker_state.btn_default_rect.collidepoint((lx, ly)):
            self.controller._set_default(map)
            return True
        if self.picker_state.btn_close_rect and self.picker_state.btn_close_rect.collidepoint((lx, ly)):
            self.controller._close()
            return True
        return False

    def _handle_tileset_filter_click(self, lx, ly):
        """Toggle the tileset filter checkbox; reset input and reload assets when unchecked."""
        # Toggle filtro 'tileset'
        if self.picker_state.tileset_checkbox_rect and self.picker_state.tileset_checkbox_rect.collidepoint((lx, ly)):
            self.picker_state.tileset_filter = not self.picker_state.tileset_filter
            # Reset input and source when toggling filter
            self.picker_state.tileset_input_active = False
            self.picker_state.tileset_source = None
            if not self.picker_state.tileset_filter:
                self.controller._load_assets()
            return True
        return False

    def _handle_tileset_input_click(self, lx, ly):
        """Activate the grid-size text input widget when clicking its bounding rectangle."""
        # Activar input de tamaño del grid
        if self.picker_state.tileset_filter and self.picker_state.tileset_input_rect and self.picker_state.tileset_input_rect.collidepoint((lx, ly)):
            self.picker_state.tileset_input_active = True
            self.controller.view.tileset_text_input.activate(self.picker_state.tileset_grid_size_text, True)
            return True
        return False

    def _handle_tileset_create_click(self, lx, ly, map):
        """Handle click on 'Crear tiles' button: slice selected tileset with current grid size."""
        if self.picker_state.tileset_filter and self.picker_state.btn_tileset_rect and self.picker_state.btn_tileset_rect.collidepoint((lx, ly)):
            source = self.picker_state.tileset_source
            if not source:
                return False
            grid_size = self.picker_state.tileset_grid_size
            self.controller._load_tileset_assets(source, grid_size)
            return True
        return False

    def _handle_drag_start(self, button, lx, ly):
        """Begin dragging the picker window using the right mouse button."""
        # Arrastrar ventana completa con botón derecho
        if button == 3:
            self.picker_state.dragging = True
            self.picker_state.drag_offset = (lx, ly)
            return True
        return False

    def _handle_grid_click(self, lx, ly, map):
        """Process clicks on the asset grid: directory navigation, tileset loading, or tile selection."""
        cols = COLS * 3
        col = (lx - PAD) // (THUMB + PAD)
        row = (ly - PAD + self.editor_state.scroll_offset) // (THUMB + PAD)
        idx = row * cols + col

        assets = self.controller.assets
        if not (0 <= col < cols and row >= 0 and idx < len(assets)):
            return False

        value, _, is_dir, _ = assets[idx]
        if value == "":
            return False
        if idx == 0 and self.controller.current_dir == self.controller.base_dir:
            return False

        if is_dir:
            current_time = pygame.time.get_ticks()
            if value == self.last_click_value and current_time - self.last_click_time <= 600:
                old_dir = self.controller.current_dir
                if value == "..":
                    print(f"[TilePicker] Double-click arrow: dir before {old_dir}, parent {old_dir.parent}")
                    self.controller.current_dir = old_dir.parent
                else:
                    new_dir = old_dir / value
                    print(f"[TilePicker] Double-click directory: '{value}'. Changing dir from {old_dir} to {new_dir}")
                    self.controller.current_dir = new_dir
                self.controller._load_assets()
                print(f"[TilePicker] After load, assets count: {len(self.controller.assets)}, names: {[name for name,_,_,_ in self.controller.assets]}")
                self.last_click_time = 0
                self.last_click_value = None
            else:
                print(f"[TilePicker] Single click on directory '{value}'")
                self.last_click_time = current_time
                self.last_click_value = value
            return True

        if self.picker_state.tileset_filter:
            # Slice selected tileset image into tiles
            self.picker_state.tileset_source = value
            grid_size = self.picker_state.tileset_grid_size
            self.controller._load_tileset_assets(value, grid_size)
            return True

        self.editor_state.current_choice = value
        self.picker_state.current_choice = value
        return True

    def handle_event(self, ev, camera=None, map=None):
        """
        Delegates various mouse events to picker logic.
        """
        # Handle clicks
        if ev.type == pygame.MOUSEBUTTONDOWN:
            # Skip left-click here; left-click handled in _on_mouse_down
            if ev.button != 1:
                if self.handle_click(ev.pos, ev.button, map):
                    return True
        # Handle dragging movement
        if ev.type == pygame.MOUSEMOTION and self.picker_state.dragging:
            self.controller.drag(ev.pos)
            return True
        # Stop drag on right button release
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.picker_state.dragging:
            self.controller.stop_drag()
            return True
        # Handle scroll wheel
        if ev.type == pygame.MOUSEWHEEL:
            self.controller.scroll(ev.y)
            return True
        # Entrada de teclado para tamaño de grid de tileset
        if ev.type == pygame.KEYDOWN and self.picker_state.tileset_input_active:
            # Delegate to TextInput widget
            widget = self.controller.view.tileset_text_input
            if widget.handle_event(ev):
                # Sync text to state
                self.picker_state.tileset_grid_size_text = widget.text
                # On commit (widget inactive), parse value
                if not widget.active:
                    try:
                        self.picker_state.tileset_grid_size = int(self.picker_state.tileset_grid_size_text)
                    except ValueError:
                        self.picker_state.tileset_grid_size = 0
                    self.picker_state.tileset_input_active = False
                return True
            return False
        return False