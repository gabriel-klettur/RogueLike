import pygame

class TileEditorView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor     = editor_state

    def render(self, screen, camera, map):
        if not self.editor.active:
            return

        # Render title panel
        self.controller.title_controller.render(screen)
        
        self.controller.toolbar.view.render(screen)
        # Normal tile picker
        if self.editor.picker_state.open:
            self.controller.picker.view.render(screen)

        # Tiles View Panel
        if self.editor.toolbar_state.view_active:
            self.controller.view_panel_controller.render(screen, camera, map)
            
        # Render layers panel
        if self.editor.toolbar_state.layers_view_open:
            self.controller.layers_panel_controller.render(screen)
            
        # Render collision panel
        if self.editor.toolbar_state.collision_picker_open:
            self.controller.collision_panel_controller.render(screen)
            
        # Outline 
        self.controller.outline_view.render(screen, camera, map)    