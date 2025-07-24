import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD, CLR_SELECTION, CLR_BORDER


class SizePanelView:
    """
    Vista para el panel de selección de tamaño.
    Separa responsabilidades en métodos: posición, render de opciones, y borde.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        """
        Dibuja el panel solo si está visible.
        """
        if not self.state.visible:
            return

        mouse_pos = pygame.mouse.get_pos()
        font = pygame.font.SysFont("Arial", 14)

        x0, y0 = self._get_panel_position()
        self.state.option_rects.clear()

        # Render de cada opción de tamaño
        self._render_size_options(screen, mouse_pos, font, x0, y0)

        # Borde exterior
        total_height = len(self.state.sizes) * BTN_H
        border_rect = pygame.Rect(x0, y0, BTN_W, total_height)
        pygame.draw.rect(screen, CLR_SELECTION, border_rect, 3)

    def _get_panel_position(self):
        """
        Calcula la posición inicial del panel (override draggable o junto a toolbar).
        """
        if self.state.pos is not None:
            return self.state.pos

        toolbar = self.controller.editor_controller.toolbar
        x0 = toolbar.x + toolbar.size + toolbar.padding
        y0 = toolbar.y
        self.state.pos = (x0, y0)
        return x0, y0

    def _render_size_options(self, screen, mouse_pos, font, x0, y0):
        """
        Dibuja cada botón de tamaño, aplica hover y selección.
        """
        for idx, (w, h) in enumerate(self.state.sizes):
            rect = pygame.Rect(x0, y0 + idx * BTN_H, BTN_W, BTN_H)
            is_selected = idx == self.state.selected_index

            self._draw_option_button(screen, rect, is_selected, f"{w}x{h}", mouse_pos, font)
            self.state.option_rects[idx] = rect

    def _draw_option_button(self, screen, rect, selected, label, mouse_pos, font):
        """
        Dibuja un botón con estado seleccionado y hover overlay.
        """
        bg_color = CLR_SELECTION if selected else (20, 20, 20)
        text_color = (0, 0, 0) if selected else (255, 255, 255)

        # Fondo y borde
        pygame.draw.rect(screen, bg_color, rect)
        pygame.draw.rect(screen, CLR_BORDER, rect, 2)

        # Overlay de hover
        if rect.collidepoint(mouse_pos):
            hover_surf = pygame.Surface((BTN_W, BTN_H), pygame.SRCALPHA)
            hover_surf.fill((255, 255, 0, 100))
            screen.blit(hover_surf, rect.topleft)

        # Texto centrado verticalmente
        text_surf = font.render(label, True, text_color)
        text_x = rect.x + PAD
        text_y = rect.y + (BTN_H - text_surf.get_height()) // 2
        screen.blit(text_surf, (text_x, text_y))
