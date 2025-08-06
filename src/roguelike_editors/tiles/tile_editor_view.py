import pygame

class TileEditorView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor     = editor_state

        # Cache font and indicator resources for current layer display
        self.indicator_font = pygame.font.SysFont("Arial", 24, bold=True)
        self.indicator_text_color = (255, 215, 0)
        self.indicator_shadow_color = (0, 0, 0)
        self.indicator_padding = 20
        self.indicator_bg_pad = (16, 12)
        self._indicator_cache = {}  # cache label to (text_surf, shadow_surf, bg_surf)

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
        # Indicator of current layer (always visible while editor active)
        if self.editor.active:
            layer = self.editor.current_layer
            label = f"{layer.value}: {layer.name}"
            # Render current layer indicator using cached surfaces
            cache = self._indicator_cache
            if label not in cache:
                text_surf = self.indicator_font.render(label, True, self.indicator_text_color)
                shadow_surf = self.indicator_font.render(label, True, self.indicator_shadow_color)
                bg_rect_temp = text_surf.get_rect()
                bg_rect = bg_rect_temp.inflate(*self.indicator_bg_pad)
                bg_surf = pygame.Surface(bg_rect.size, pygame.SRCALPHA)
                bg_surf.fill((30, 30, 30, 180))
                pygame.draw.rect(bg_surf, self.indicator_text_color, bg_surf.get_rect(), 2, border_radius=8)
                cache[label] = (text_surf, shadow_surf, bg_surf)
            text_surf, shadow_surf, bg_surf = cache[label]
            screen_w, screen_h = screen.get_size()
            text_rect = text_surf.get_rect(midbottom=(screen_w // 2, screen_h - self.indicator_padding))
            # Background
            bg_rect = text_rect.inflate(*self.indicator_bg_pad)
            screen.blit(bg_surf, (bg_rect.left, bg_rect.top))
            # Shadow
            shadow_rect = text_rect.move(2, 2)
            screen.blit(shadow_surf, shadow_rect)
            # Text
            screen.blit(text_surf, text_rect)