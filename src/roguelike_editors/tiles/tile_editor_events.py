import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.common.events import cycle_enum

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_events import TilePickerEventHandler
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_events import TileToolbarEventHandler
from roguelike_editors.tiles.tiles_view_panel.tiles_view_events import TilesViewPanelEventHandler
from roguelike_editors.tiles.tiles_title.tiles_tiles_events import TilesTitleEventHandler
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_events import TilesCollisionPanelEventHandler
from roguelike_editors.tiles.layers_panel.layers_panel_events import LayersPanelEventHandler
from roguelike_editors.tiles.size_panel.size_panel_events import SizePanelEventHandler

class TileEditorEventHandler:
    """
    Maneja eventos para el Tile Editor en modo MVC.
    """
    def __init__(self, state, editor_state, controller):
        self.state = state
        self.editor_state = editor_state
        self.controller = controller

        self.picker_tool = TilePickerEventHandler(
            picker_controller = controller.picker,
            editor_state      = editor_state,
            picker_state      = controller.picker.picker_state
        )
        self.toolbar_tool = TileToolbarEventHandler(
            toolbar_controller = controller.toolbar
        )        
        self.view_panel_tool = TilesViewPanelEventHandler(
            controller.view_panel_controller,
            editor_state.view_panel_state
        )
        self.title_tool = TilesTitleEventHandler(
            controller.title_controller,
            editor_state.title_state
        )
        self.collision_panel_tool = TilesCollisionPanelEventHandler(
            controller.collision_panel_controller,
            editor_state.collision_panel_state
        )
        self.layers_panel_tool = LayersPanelEventHandler(
            controller.layers_panel_controller,
            editor_state.layers_panel_state
        )
        self.size_panel_tool = SizePanelEventHandler(
            controller.size_panel_controller,
            editor_state.size_panel_state
        )
        # Camera panning via middle mouse button
        self.panning = False
        self.pan_start = (0, 0)
        self.pan_offset_start = (0, 0)

    def handle(self, events, camera, map):
        """Reenvía cada evento al manejador correspondiente."""
        for ev in events:
            if ev.type == pygame.QUIT:
                self._on_quit(ev)
            elif ev.type == pygame.KEYDOWN:
                self._on_keydown(ev)
            elif ev.type == pygame.MOUSEBUTTONDOWN:
                # Batch brush start
                if ev.button == 1 and self.editor_state.current_tool == "brush":
                    self.controller.start_brush()
                self._on_mouse_down(ev, camera, map)
            elif ev.type == pygame.MOUSEMOTION:
                self._on_mouse_motion(ev, camera, map)
            elif ev.type == pygame.MOUSEBUTTONUP:
                # Batch flush for brush/default/delete before resetting flags
                if ev.button == 1:
                    tool = self.editor_state.current_tool
                    if tool == "brush" and self.editor_state.brush_dragging:
                        self.controller.flush_brush(map, camera)
                    if tool == "default" and getattr(self.editor_state, 'default_dragging', False):
                        self.controller.flush_brush(map, camera)
                    if tool == "delete" and getattr(self.editor_state, 'delete_dragging', False):
                        self.controller.flush_brush(map, camera)
                self._on_mouse_up(ev, camera, map)
            elif ev.type == pygame.MOUSEWHEEL:
                self._on_mouse_wheel(ev, camera)
            # Delegate to panel event handlers
            # Toolbar drag events
            self.toolbar_tool.handle_event(ev)
            if self.editor_state.toolbar_state.view_active:
                self.view_panel_tool.handle_event(ev, camera, map)
            if self.editor_state.toolbar_state.layers_view_open:
                self.layers_panel_tool.handle_event(ev, camera, map)
            if self.editor_state.toolbar_state.collision_picker_open:
                self.collision_panel_tool.handle_event(ev, camera, map)
            # Size panel drag and events
            if self.editor_state.size_panel_state.visible:
                self.size_panel_tool.handle_event(ev)
            if self.editor_state.picker_state.open:
                self.picker_tool.handle_event(ev, camera, map)
            # Always forward to title panel
            self.title_tool.handle_event(ev)

    def _on_quit(self, ev):        
        self.state.running = False

    def _on_keydown(self, ev):
        if ev.key == pygame.K_ESCAPE:            
            self.editor_state.active = False            
            self.editor_state.selected_tile = None
            self.editor_state.picker_state.open = False
            self.editor_state.brush_dragging = False
            self.editor_state.default_dragging = False
            self.editor_state.delete_dragging = False
        elif ev.key == pygame.K_F8:
            new_val = not self.editor_state.active
            self.editor_state.active = new_val            
            if new_val:
                # Al activar el editor con F8, abrir panel de vista y panel de tamaño
                self.editor_state.toolbar_state.view_active = True
                self.editor_state.size_panel_state.visible = True
            if not new_val:
                self.editor_state.picker_state.open = False
                self.editor_state.selected_tile = None
                self.editor_state.brush_dragging = False
        elif ev.key == pygame.K_b:
            self.editor_state.toolbar_state.show_buildings = not self.editor_state.toolbar_state.show_buildings

    def _on_mouse_down(self, ev, camera, map):
        # Consume clicks dentro del Tile Picker para evitar click-through al mapa
        pos = ev.pos
        if self.editor_state.picker_state.open and self.controller.picker.is_over(pos):
            self.picker_tool.handle_click(pos, ev.button, map)
            return

        
        # Pan camera with middle mouse
        if ev.button == 2:
            self.panning = True
            self.pan_start = ev.pos
            self.pan_offset_start = (camera.offset_x, camera.offset_y)
            return
        pos = ev.pos
        # 1) Toolbar click
        if self.toolbar_tool.handle_click(ev, map, camera):
            return
        # Handle size panel clicks
        if self.size_panel_tool.handle_event(ev):
            return
        # Collision panel: interceptar clicks para evitar propagación al mapa
        if self.editor_state.toolbar_state.collision_picker_open and self.collision_panel_tool.handle_event(ev):
            return

        tool = self.editor_state.current_tool
        # Delete tool action: start drag and apply delete
        if tool == "delete" and ev.button == 1:
            tile = self.controller._tile_under_mouse(pos, camera, map)
            if tile:
                self.editor_state.selected_tile = tile
                # start a batched operation
                self.controller.start_brush()
                self.controller.toolbar.delete_tile(map, camera)
                self.editor_state.delete_dragging = True
            return
        # Default tool action: restore area to default at clicked position and start drag
        if tool == "default" and ev.button == 1:
            tile = self.controller._tile_under_mouse(pos, camera, map)
            if tile:
                self.editor_state.selected_tile = tile
                # start a batched operation
                self.controller.start_brush()
                self.controller.toolbar.set_default(map, camera)
                # Start drag for default tool
                self.editor_state.default_dragging = True
            return
        # 2) Select
        if tool == "select" and ev.button == 1:
            if self.editor_state.picker_state.open:
                if not self.picker_tool.handle_click(pos, button=1, map=map):
                    self.controller.select_tile_at(pos, camera, map)
            else:
                self.controller.select_tile_at(pos, camera, map)

        # 3) Brush
        elif tool == "brush" and ev.button == 1:
            if self.editor_state.picker_state.open and self.controller.picker.is_over(pos):
                if self.picker_tool.handle_click(pos, button=1, map=map):
                    return
            # Start a new brush stroke to reset throttle and pending sets
            self.controller.start_brush()
            self.editor_state.brush_dragging = True
            self.controller.apply_brush(pos, camera, map)

        # 4) Eyedropper
        elif tool == "eyedropper" and ev.button == 1:
            self.controller.apply_eyedropper(pos, camera, map)



    def _on_mouse_motion(self, ev, camera, map):

        pos = ev.pos
        # Handle camera panning
        if self.panning:
            dx = ev.pos[0] - self.pan_start[0]
            dy = ev.pos[1] - self.pan_start[1]
            camera.offset_x = self.pan_offset_start[0] - dx / camera.zoom
            camera.offset_y = self.pan_offset_start[1] - dy / camera.zoom
            return
        # Brush drag
        if self.editor_state.current_tool == "brush" and self.editor_state.brush_dragging:
            if not (self.editor_state.picker_state.open and self.controller.picker.is_over(pos)):
                self.controller.apply_brush(pos, camera, map)
        # Default drag (apply default continuously while dragging)
        if self.editor_state.current_tool == "default" and getattr(self.editor_state, 'default_dragging', False):
            if not (self.editor_state.picker_state.open and self.controller.picker.is_over(pos)):
                tile = self.controller._tile_under_mouse(pos, camera, map)
                if tile:
                    self.editor_state.selected_tile = tile
                    self.controller.toolbar.set_default(map, camera)
        # Delete drag (apply delete continuously while dragging)
        if self.editor_state.current_tool == "delete" and getattr(self.editor_state, 'delete_dragging', False):
            if not (self.editor_state.picker_state.open and self.controller.picker.is_over(pos)):
                tile = self.controller._tile_under_mouse(pos, camera, map)
                if tile:
                    self.editor_state.selected_tile = tile
                    self.controller.toolbar.delete_tile(map, camera)


    def _on_mouse_up(self, ev, camera, map):

        # Release brush
        if ev.button == 1 and self.editor_state.current_tool == "brush":
            self.editor_state.brush_dragging = False
        # Release default drag
        if ev.button == 1 and self.editor_state.current_tool == "default":
            self.editor_state.default_dragging = False
        # Release delete drag
        if ev.button == 1 and self.editor_state.current_tool == "delete":
            self.editor_state.delete_dragging = False
        # Stop camera panning
        if ev.button == 2 and self.panning:
            self.panning = False


    def _on_mouse_wheel(self, ev, camera):
        # Zoom entero con rueda cuando botón medio presionado
        if self.panning:
            current = int(camera.zoom)
            if ev.y > 0:
                camera.zoom = current + 1
            elif ev.y < 0:
                camera.zoom = max(1, current - 1)
            return
        # Ciclar capas si estamos en modo brush
        if self.editor_state.current_tool == "brush":
            self.editor_state.current_layer = cycle_enum(self.editor_state.current_layer, 1 if ev.y > 0 else -1, Layer)
            return
        # Cambiar layer seleccionado con rueda cuando panel de vista activo
        if self.editor_state.toolbar_state.view_active:
            self.editor_state.current_layer = cycle_enum(self.editor_state.current_layer, 1 if ev.y > 0 else -1, Layer)
            return