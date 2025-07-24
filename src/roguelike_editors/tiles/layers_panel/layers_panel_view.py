import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_selection_border
from roguelike_ui.widgets.button import Button

class LayersPanelView:
    """View for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        self.panel = DraggablePanel(BTN_W, BTN_H)

    def render(self, screen):
        # Render layers panel as popup menu

        mouse_pos = pygame.mouse.get_pos()
        font = pygame.font.SysFont("Arial", 14)
        # Compute panel size and prepare surface
        panel_height = (len(list(Layer)) + 1) * BTN_H
        self.panel.resize(BTN_W, panel_height)
        # Initialize position
        if self.panel.pos is None:
            if self.state.pos is not None:
                self.panel.pos = self.state.pos
            else:
                toolbar = self.controller.editor_controller.toolbar
                icon_rect = toolbar.icon_rects.get("view_layers")
                if icon_rect:
                    x = icon_rect.right + toolbar.padding
                    y = icon_rect.y
                else:
                    x = toolbar.x + toolbar.size + PAD
                    y = toolbar.y
                self.panel.pos = (x, y)
                self.state.pos = self.panel.pos
        # Draw panel background
        screen.blit(self.panel.surface, self.panel.pos)
        # Use panel.pos for rendering
        x0, y0 = self.panel.pos
        # Clear previous rects
        self.state.option_rects.clear()
        # Render layer toggles
        for i, layer in enumerate(Layer):
            ry = y0 + i * BTN_H
            rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
            visible = self.state.visible_layers.get(layer, False)
            btn_bg = (0, 255, 0, 100) if visible else (255, 0, 0, 100)
            btn_border = (0, 255, 0) if visible else (255, 0, 0)
            btn = Button(rect, bgcolor=btn_bg, border_color=btn_border, hover_color=(255,255,0,100), border_width=2)
            btn.is_hovered(mouse_pos)
            btn.draw(screen)
            text_color = (0, 0, 0) if visible else (255, 255, 255)
            text = font.render(layer.name, True, text_color)
            ty = ry + (BTN_H - text.get_height()) // 2
            screen.blit(text, (x0 + 5, ty))
            self.state.option_rects[layer] = rect
        # Render buildings toggle
        ry = y0 + len(list(Layer)) * BTN_H
        rect = pygame.Rect(x0, ry, BTN_W, BTN_H)
        sb = self.controller.editor_state.toolbar_state.show_buildings
        btn_bg = (0, 255, 0, 100) if sb else (255, 0, 0, 100)
        btn_border = (128, 0, 128) if sb else (255, 0, 0)
        btn = Button(rect, bgcolor=btn_bg, border_color=btn_border, hover_color=(255,255,0,100), border_width=2)
        btn.is_hovered(mouse_pos)
        btn.draw(screen)
        text_color = (0, 0, 0) if sb else (255, 255, 255)
        text = font.render("Buildings", True, text_color)
        ty = ry + (BTN_H - text.get_height()) // 2
        screen.blit(text, (x0 + 5, ty))
        self.state.option_rects["buildings"] = rect
        panel_h = (len(list(Layer)) + 1) * BTN_H
        draw_selection_border(screen, pygame.Rect(x0, y0, BTN_W, panel_h), CLR_SELECTION, thickness=3)
