import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION, CLR_BORDER
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.button import Button
from roguelike_ui.widgets.hover import draw_selection_border


class SizePanelView:
    """
    Vista para el panel de selección de tamaño.
    Separa responsabilidades en métodos: posición, render de opciones, y borde.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        self.panel = DraggablePanel(BTN_W, BTN_H)

    def render(self, screen):
        """
        Dibuja el panel solo si está visible.
        """
        if not self.state.visible:
            return

        mouse_pos = pygame.mouse.get_pos()
        font = pygame.font.SysFont("Arial", 14)

        # Ajustar tamaño del panel
        panel_height = len(self.state.sizes) * BTN_H
        self.panel.resize(BTN_W, panel_height)

        # Asegurar posición del panel
        self._ensure_panel_position()
        x0, y0 = self.panel.pos

        # Dibujar fondo del panel
        screen.blit(self.panel.surface, self.panel.pos)

        # Resetear rects clicables
        self.state.option_rects.clear()

        # Renderizar opciones de tamaño con botones
        self._render_size_buttons(screen, mouse_pos, font, x0, y0)

        # Dibujar borde de selección alrededor del panel
        border_rect = pygame.Rect(x0, y0, BTN_W, panel_height)
        draw_selection_border(screen, border_rect, CLR_SELECTION, thickness=3)

    def _ensure_panel_position(self):
        """
        Calcula y asigna la posición inicial del panel si no existe.
        """
        if self.state.pos is None:
            toolbar = self.controller.editor_controller.toolbar
            x0 = toolbar.x + toolbar.size + toolbar.padding
            y0 = toolbar.y
            self.state.pos = (x0, y0)
        self.panel.pos = self.state.pos

    def _render_size_buttons(self, screen, mouse_pos, font, x0, y0):
        """
        Dibuja los botones de tamaño usando Button de roguelike_ui.
        """
        for idx, (w, h) in enumerate(self.state.sizes):
            rect = pygame.Rect(x0, y0 + idx * BTN_H, BTN_W, BTN_H)
            is_selected = idx == self.state.selected_index

            # Configurar botón
            btn = Button(
                rect,
                bgcolor=CLR_SELECTION if is_selected else (20, 20, 20),
                border_color=CLR_SELECTION if is_selected else CLR_BORDER,
                hover_color=(255, 255, 0, 100),
                border_width=2
            )
            btn.is_hovered(mouse_pos)
            btn.draw(screen)

            # Texto centrado verticalmente
            text_color = (0, 0, 0) if is_selected else (255, 255, 255)
            text_surf = font.render(f"{w}x{h}", True, text_color)
            text_x = rect.x + PAD
            text_y = rect.y + (BTN_H - text_surf.get_height()) // 2
            screen.blit(text_surf, (text_x, text_y))

            self.state.option_rects[idx] = rect
