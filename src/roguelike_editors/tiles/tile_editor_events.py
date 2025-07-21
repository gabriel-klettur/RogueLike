import pygame
from roguelike_engine.map.model.layer import Layer

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_events import TilePickerEventHandler
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_events import TileToolbarEventHandler
from roguelike_editors.tiles.tiles_view_panel.tiles_view_events import TilesViewPanelEventHandler
from roguelike_editors.tiles.tiles_title.tiles_tiles_events import TilesTitleEventHandler
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_events import TilesCollisionPanelEventHandler
from roguelike_editors.tiles.layers_panel.layers_panel_events import LayersPanelEventHandler

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
                self._on_mouse_up(ev)
                # Batch brush flush
                if ev.button == 1 and self.editor_state.current_tool == "brush":
                    self.controller.flush_brush(map)
            elif ev.type == pygame.MOUSEWHEEL:
                self._on_mouse_wheel(ev)
            # Delegate to panel event handlers
            if self.editor_state.toolbar_state.view_active:
                self.view_panel_tool.handle_event(ev, camera, map)
            if self.editor_state.toolbar_state.layers_view_open:
                self.layers_panel_tool.handle_event(ev, camera, map)
            if self.editor_state.toolbar_state.collision_picker_open:
                self.collision_panel_tool.handle_event(ev, camera, map)
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
        elif ev.key == pygame.K_F8:
            new_val = not self.editor_state.active
            self.editor_state.active = new_val            
            if not new_val:
                self.editor_state.picker_state.open = False
                self.editor_state.selected_tile = None
                self.editor_state.brush_dragging = False
        elif ev.key == pygame.K_b:
            self.editor_state.toolbar_state.show_buildings = not self.editor_state.toolbar_state.show_buildings

    def _on_mouse_down(self, ev, camera, map):
        
        pos = ev.pos
        # 1) Toolbar click
        if self.toolbar_tool.handle_click(ev):
            return



        tool = self.editor_state.current_tool
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
            self.editor_state.brush_dragging = True
            self.controller.apply_brush(pos, camera, map)

        # 4) Eyedropper
        elif tool == "eyedropper" and ev.button == 1:
            self.controller.apply_eyedropper(pos, camera, map)



    def _on_mouse_motion(self, ev, camera, map):
        pos = ev.pos
        # Brush drag
        if self.editor_state.current_tool == "brush" and self.editor_state.brush_dragging:
            if not (self.editor_state.picker_state.open and self.controller.picker.is_over(pos)):
                self.controller.apply_brush(pos, camera, map)


    def _on_mouse_up(self, ev):
        # Release brush
        if ev.button == 1 and self.editor_state.current_tool == "brush":
            self.editor_state.brush_dragging = False


    def _on_mouse_wheel(self, ev):
        # Ciclar capas si estamos en modo brush
        if self.editor_state.current_tool == "brush":
            layers = list(Layer)
            idx = layers.index(self.editor_state.current_layer)
            new_idx = (idx + (1 if ev.y > 0 else -1)) % len(layers)
            self.editor_state.current_layer = layers[new_idx]            
            return
        # Cambiar layer seleccionado con rueda cuando panel de vista activo
        if self.editor_state.toolbar_state.view_active:
            layers = list(Layer)
            idx = layers.index(self.editor_state.current_layer)
            new_idx = (idx + (1 if ev.y > 0 else -1)) % len(layers)
            self.editor_state.current_layer = layers[new_idx]
            return