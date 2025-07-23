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
        # Render brush size panel if visible
        if self.controller.editor.size_panel_state.visible:
            self.controller.size_panel_controller.render(screen)
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
        # Mostrar indicador de capa centrado en la parte inferior en modo brush
        if self.editor.current_tool == "brush":
            font = pygame.font.SysFont("Arial", 32)
            layer_name = self.editor.current_layer.name
            text_surf = font.render(layer_name, True, (255, 255, 0))
            screen_w, screen_h = screen.get_size()
            padding = 10
            text_rect = text_surf.get_rect()
            text_rect.midbottom = (screen_w // 2, screen_h - padding)
            bg_rect = text_rect.inflate(8, 8)
            pygame.draw.rect(screen, (0, 0, 0), bg_rect)
            screen.blit(text_surf, text_rect)    