# Path: src/roguelike_game/systems/editor/tiles/controller/tile_editor_events.py
import pygame

class TileEditorEventHandler:
    """
    Maneja eventos para el Tile Editor en modo MVC.
    """
    def __init__(self, state, editor_state, controller):
        self.state = state
        self.editor_state = editor_state
        self.controller = controller

    def handle(self, camera, map):
        """Reenvía cada evento al manejador correspondiente."""
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                self._on_quit(ev)
            elif ev.type == pygame.KEYDOWN:
                self._on_keydown(ev)
            elif ev.type == pygame.MOUSEBUTTONDOWN:
                self._on_mouse_down(ev,camera, map)
            elif ev.type == pygame.MOUSEMOTION:
                self._on_mouse_motion(ev, camera, map)
            elif ev.type == pygame.MOUSEBUTTONUP:
                self._on_mouse_up(ev)
            elif ev.type == pygame.MOUSEWHEEL:
                self._on_mouse_wheel(ev)

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

    def _on_mouse_down(self, ev, camera, map):
        pos = ev.pos
        # 1) Toolbar click
        if ev.button == 1 and self.controller.toolbar.handle_click(pos):
            return

        tool = self.editor_state.current_tool
        # 2) Select
        if tool == "select" and ev.button == 1:
            if self.editor_state.picker_state.open:
                if not self.controller.picker.handle_click(pos, button=1, map=map):
                    self.controller.select_tile_at(pos, camera, map)
            else:
                self.controller.select_tile_at(pos, camera, map)

        # 3) Brush
        elif tool == "brush" and ev.button == 1:
            if self.editor_state.picker_state.open and self.controller.picker.is_over(pos):
                if self.controller.picker.handle_click(pos, button=1, map=map):
                    return
            self.editor_state.brush_dragging = True
            self.controller.apply_brush(pos, camera, map)

        # 4) Eyedropper
        elif tool == "eyedropper" and ev.button == 1:
            self.controller.apply_eyedropper(pos, camera, map)

        # 5) Palette drag
        elif ev.button == 3 and self.editor_state.picker_state.open:
            if self.controller.picker.handle_click(pos, button=3, map=map):
                return

    def _on_mouse_motion(self, ev, camera, map):
        pos = ev.pos
        # Brush drag
        if self.editor_state.current_tool == "brush" and self.editor_state.brush_dragging:
            if not (self.editor_state.picker_state.open and self.controller.picker.is_over(pos)):
                self.controller.apply_brush(pos, camera, map)
        # Palette drag
        elif self.editor_state.picker_state.open and self.editor_state.picker_state.dragging:
            self.controller.picker.drag(pos)

    def _on_mouse_up(self, ev):
        # Release brush
        if ev.button == 1 and self.editor_state.current_tool == "brush":
            self.editor_state.brush_dragging = False
        # Stop palette drag
        if ev.button == 3 and self.editor_state.picker_state.open:
            self.controller.picker.stop_drag()

    def _on_mouse_wheel(self, ev):
        if self.editor_state.picker_state.open:
            self.controller.picker.scroll(ev.y)