import pygame
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, CLR_SELECTION, CLR_HOVER
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_selection_border
from roguelike_ui.ui_helpers import draw_highlight_rect


class TilesCollisionPanelView:
    """
    Vista para el panel de colisiones de tiles.
    Separa responsabilidades en métodos: cálculo de tamaño y posición,
    renderizado de opciones, sombra y fondo, y dibujo final.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        self.options = [("#", "Collision"), (".", "Walk")]
        self.panel = DraggablePanel(1, 1, bgcolor=(20, 20, 20, 200))

    def render(self, screen):
        """
        Dibuja el panel completo con sombras, fondo y opciones.
        """
        ts = self.controller.editor_state.toolbar_state
        if not ts.collision_picker_open:
            return

        # Calcular dimensiones y ajustar panel
        w, h = self._compute_dimensions(screen)
        self.panel.resize(w, h)

        # Posicionar panel
        pos_x, pos_y = self._ensure_panel_position(screen, w, h)

        # Sombra
        self._draw_shadow(screen, pos_x, pos_y, w, h)

        # Dibujar panel


        # Dibujar opciones
        self.state.option_rects.clear()
        self._render_options(self.panel.surface, pos_x, pos_y)

        # Borde exterior
        draw_selection_border(self.panel.surface, self.panel.surface.get_rect(), CLR_SELECTION, thickness=3)
        screen.blit(self.panel.surface, (pos_x, pos_y))

    def _compute_dimensions(self, screen):
        """
        Calcula ancho y alto del panel según opciones y fuentes.
        """
        label_font = pygame.font.SysFont("Arial", 14)
        w = len(self.options) * (THUMB + PAD) + PAD
        h = THUMB + PAD + label_font.get_height() + PAD
        return w, h

    def _compute_position(self, screen, w, h):
        """
        Determina posición del panel: draggable o alineado al icono.
        """
        ts = self.controller.editor_state.toolbar_state
        if ts.collision_picker_pos:
            return ts.collision_picker_pos

        icon_rect = self.controller.editor_controller.toolbar.icon_rects.get('view_collisions')
        if icon_rect:
            tb = self.controller.editor_controller.toolbar
            x = icon_rect.right + tb.padding
            y = icon_rect.y
        else:
            x, y = self._fallback_center(screen, w, h)

        ts.collision_picker_pos = (x, y)
        return x, y

    def _fallback_center(self, screen, w, h):
        """
        Posición por defecto si no hay icono o panel previo.
        Centrado en pantalla o bajo view panel si existe.
        """
        editor_ctrl = self.controller.editor_controller
        vp_state = getattr(editor_ctrl.view_panel_controller, 'state', None)
        if vp_state and getattr(vp_state, 'pos', None) and getattr(vp_state, 'size', None):
            x_vp, y_vp = vp_state.pos
            _, h_vp = vp_state.size
            return x_vp, y_vp + h_vp + PAD
        sw, sh = screen.get_size()
        return (sw - w) // 2, (sh - h) // 2

    def _store_panel_state(self, x, y, w, h):
        """
        Almacena dimensiones y posición para manejo de eventos.
        """
        ts = self.controller.editor_state.toolbar_state
        ts.collision_picker_panel_size = (w, h)

    def _ensure_panel_position(self, screen, w, h):
        """
        Calcula y asigna la posición del panel usando toolbar_state o icono.
        """
        pos_x, pos_y = self._compute_position(screen, w, h)
        self.panel.pos = (pos_x, pos_y)
        self._store_panel_state(pos_x, pos_y, w, h)
        return pos_x, pos_y

    def _draw_shadow(self, screen, x, y, w, h):
        """
        Dibuja sombra bajo el panel.
        """
        shadow = pygame.Surface((w, h), pygame.SRCALPHA)
        shadow.fill((0, 0, 0, 100))
        screen.blit(shadow, (x + 4, y + 4))

    def _create_background_surface(self, w, h):
        """
        Crea superficie de fondo semitransparente del panel.
        """
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((20, 20, 20, 200))
        return surf

    def _render_options(self, surf, origin_x, origin_y):
        """
        Dibuja los iconos de opción y sus etiquetas, manejando hover y selección.
        """
        label_font = pygame.font.SysFont("Arial", 14)
        char_font = pygame.font.SysFont("Arial", THUMB)
        mouse_pos = pygame.mouse.get_pos()

        for idx, (ch, label) in enumerate(self.options):
            x = PAD + idx * (THUMB + PAD)
            y = PAD
            # Render caracter
            color = (255, 0, 0) if ch == "#" else (200, 200, 200)
            text = char_font.render(ch, True, color)
            surf.blit(text, (x + (THUMB - text.get_width()) // 2,
                             y + (THUMB - text.get_height()) // 2))

            abs_rect = pygame.Rect(origin_x + x, origin_y + y, THUMB, THUMB)
            self.state.option_rects[ch] = abs_rect

            # Hover overlay
            if abs_rect.collidepoint(mouse_pos):
                hover = pygame.Surface((THUMB, THUMB), pygame.SRCALPHA)
                hover.fill((255, 255, 0, 100))
                surf.blit(hover, (x, y))
                pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
            # Selection border
            elif self.controller.editor_state.toolbar_state.collision_choice == ch:
                pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)

            # Label
            lbl = label_font.render(label, True, (255, 255, 255))
            surf.blit(lbl, (x + (THUMB - lbl.get_width()) // 2,
                            y + THUMB + PAD))
