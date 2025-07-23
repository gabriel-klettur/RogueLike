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
        # Indicator of current layer in brush mode (professional design)
        if self.editor.current_tool == "brush":
            layer = self.editor.current_layer
            label = f"{layer.value}: {layer.name}"
            font = pygame.font.SysFont("Arial", 24, bold=True)
            text_color = (255, 215, 0)
            shadow_color = (0, 0, 0)
            text_surf = font.render(label, True, text_color)
            shadow_surf = font.render(label, True, shadow_color)
            screen_w, screen_h = screen.get_size()
            padding = 20
            text_rect = text_surf.get_rect(midbottom=(screen_w // 2, screen_h - padding))
            # Background with semi-transparency and rounded border
            bg_pad_x, bg_pad_y = 16, 12
            bg_rect = text_rect.inflate(bg_pad_x, bg_pad_y)
            bg_surf = pygame.Surface(bg_rect.size, pygame.SRCALPHA)
            bg_surf.fill((30, 30, 30, 180))
            pygame.draw.rect(bg_surf, text_color, bg_surf.get_rect(), 2, border_radius=8)
            # Position and blit background
            bg_pos = (bg_rect.left, bg_rect.top)
            screen.blit(bg_surf, bg_pos)
            # Shadow
            shadow_rect = text_rect.copy()
            shadow_rect.move_ip(2, 2)
            screen.blit(shadow_surf, shadow_rect)
            # Text
            screen.blit(text_surf, text_rect)    