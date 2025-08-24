import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_selection_border
from roguelike_ui.widgets.button import Button


class LayersPanelView:
    """
    Vista para el panel de capas.
    Separa responsabilidades en métodos claros: posición, render de capas,
    toggle de buildings y borde de selección.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        self.panel = DraggablePanel(BTN_W, BTN_H)

    def render(self, screen):
        """
        Dibuja el panel de capas en pantalla.
        """
        mouse_pos = pygame.mouse.get_pos()
        font = pygame.font.SysFont("Arial", 14)

        panel_height = (len(list(Layer)) + 1) * BTN_H
        self.panel.resize(BTN_W, panel_height)

        self._ensure_panel_position()
        x0, y0 = self.panel.pos

        self._draw_panel_background(screen)

        # Resetear rects clicables
        self.state.option_rects.clear()

        # Renderizar cada capa como botón
        self._render_layer_buttons(screen, mouse_pos, font, x0, y0)

        # Renderizar toggle de buildings
        self._render_buildings_button(screen, mouse_pos, font, x0, y0 + len(list(Layer)) * BTN_H)

        # Dibujar borde de selección alrededor del panel completo
        total_height = panel_height
        border_rect = pygame.Rect(x0, y0, BTN_W, total_height)
        draw_selection_border(screen, border_rect, CLR_SELECTION, thickness=3)

    def _ensure_panel_position(self):
        """
        Calcula y asigna la posición inicial del panel. Si no existe state.pos, lo crea.
        """
        # Inicializar posición si no existe
        if self.state.pos is None:
            toolbar = self.controller.editor_controller.toolbar
            icon_rect = toolbar.icon_rects.get("view_layers")
            if icon_rect:
                x = icon_rect.right + toolbar.padding
                y = icon_rect.y
            else:
                x = toolbar.x + toolbar.size + PAD
                y = toolbar.y
            self.state.pos = (x, y)
        # Sincronizar panel.pos con state.pos
        self.panel.pos = self.state.pos

    def _draw_panel_background(self, screen):
        """
        Dibuja el fondo del panel usando la superficie del DraggablePanel.
        """
        screen.blit(self.panel.surface, self.panel.pos)
        # Registrar bloqueador UI para suprimir hover debajo del panel
        panel_rect = pygame.Rect(self.panel.pos, self.panel.surface.get_size())
        register_blocker(panel_rect)

    def _render_layer_buttons(self, screen, mouse_pos, font, x0, y0):
        """
        Dibuja los botones para cada layer del modelo.
        """
        for idx, layer in enumerate(Layer):
            rect = pygame.Rect(x0, y0 + idx * BTN_H, BTN_W, BTN_H)
            visible = self.state.visible_layers.get(layer, False)
            self._draw_button(screen, rect, visible, layer.name, mouse_pos, font)
            self.state.option_rects[layer] = rect

    def _render_buildings_button(self, screen, mouse_pos, font, x0, y_pos):
        """
        Dibuja el botón para toggle de buildings al final del listado de layers.
        """
        rect = pygame.Rect(x0, y_pos, BTN_W, BTN_H)
        sb = self.controller.editor_state.toolbar_state.show_buildings
        self._draw_button(screen, rect, sb, "Buildings", mouse_pos, font, border_color_active=(128, 0, 128))
        self.state.option_rects["buildings"] = rect

    def _draw_button(self, screen, rect, active, label, mouse_pos, font, *,
                     bgcolor_active=(0, 255, 0, 100), bgcolor_inactive=(255, 0, 0, 100),
                     border_color_active=(0, 255, 0), border_color_inactive=(255, 0, 0)):
        """
        Crea y dibuja un botón con estados de activo/inactivo y hover.
        """
        bgcolor = bgcolor_active if active else bgcolor_inactive
        border_color = border_color_active if active else border_color_inactive

        btn = Button(
            rect,
            bgcolor=bgcolor,
            border_color=border_color,
            hover_color=(255, 255, 0, 100),
            border_width=2
        )
        btn.is_hovered(mouse_pos)
        btn.draw(screen)

        text_color = (0, 0, 0) if active else (255, 255, 255)
        text_surf = font.render(label, True, text_color)
        text_pos = (rect.x + 5, rect.y + (BTN_H - text_surf.get_height()) // 2)
        screen.blit(text_surf, text_pos)
