import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.button import Button

import logging
logger = logging.getLogger(__name__)

class ToolbarState:
    """Estado genérico para posición y arrastre de un toolbar."""
    def __init__(self):
        self.pos = None
        self.dragging = False
        self.drag_offset = (0, 0)

class ToolbarView:
    """
    Vista genérica de toolbar con botones.

    Args:
        controller: Objeto que debe implementar is_active(tool: str) -> bool.
        items (list[str]): Claves de herramientas.
        icons (dict[str, pygame.Surface]): Map de clave a surface de icono.
        x, y (int): Posición inicial del panel.
        size (int): Tamaño de cada botón.
        padding (int): Espacio entre botones.
        bgcolor (tuple): Color de fondo del panel.
        border_color (tuple): Color del borde de los botones.
        hover_color (tuple): Color de overlay en hover.
        selection_color (tuple): Color del borde en herramienta activa.
        selection_border_width (int): Grosor del borde de selección.
    """
    def __init__(self, controller, items, icons,
                 x, y, size, padding,
                 bgcolor=(0, 0, 0, 180),
                 border_color=(255, 255, 255),
                 hover_color=(255, 255, 0, 100),
                 selection_color=(255, 255, 0),
                 selection_border_width=4, name=None):
        self.controller = controller
        self.items = items
        self.icons = icons
        self.x = x
        self.y = y
        self.size = size
        self.padding = padding
        self.bgcolor = bgcolor
        self.border_color = border_color
        self.hover_color = hover_color
        self.selection_color = selection_color
        self.selection_border_width = selection_border_width
        # Padding interno alrededor de íconos
        self.edge_padding = 8
        width = size + 2 * self.edge_padding
        height = len(items) * (size + padding) - padding + 2 * self.edge_padding
        self.panel = DraggablePanel(width, height, bgcolor)
        self.panel.pos = (x, y)
        self.name = name or self.__class__.__name__

        # Crear botones
        self.buttons = {}
        for idx, tool in enumerate(items):
            rect = pygame.Rect(0, idx * (size + padding), size, size)
            btn = Button(rect, bgcolor=(0, 0, 0, 0),
                         border_color=border_color,
                         hover_color=hover_color)
            self.buttons[tool] = btn
        self.icon_rects = {}

    def render(self, screen):
        """
        Dibuja el toolbar: panel, botones, iconos, hover y selección.
        """
        mouse_pos = pygame.mouse.get_pos()
        # Redimensionar panel según número de items
        width = self.size + 2 * self.edge_padding
        height = len(self.items) * (self.size + self.padding) - self.padding + 2 * self.edge_padding
        self.panel.resize(width, height)
        panel_pos = self.panel.pos or (self.x, self.y)

        # Ajustar rel_mouse considerando padding interior
        rel_mouse = (mouse_pos[0] - panel_pos[0] - self.edge_padding,
                     mouse_pos[1] - panel_pos[1] - self.edge_padding)
        # Dibujar botones e iconos
        for idx, tool in enumerate(self.items):
            btn = self.buttons[tool]
            # Posicionar botón con padding interior
            btn.rect.topleft = (self.edge_padding,
                                self.edge_padding + idx * (self.size + self.padding))
            btn.is_hovered(rel_mouse)
            btn.draw(self.panel.surface)
            icon_surf = self.icons[tool]
            icon_pos = (
                btn.rect.x + (self.size - icon_surf.get_width()) // 2,
                btn.rect.y + (self.size - icon_surf.get_height()) // 2
            )
            self.panel.surface.blit(icon_surf, icon_pos)
            self.icon_rects[tool] = btn.rect.move(panel_pos)
        # Blitear panel
        screen.blit(self.panel.surface, panel_pos)
        # Bloquear interacción bajo el panel
        panel_rect = pygame.Rect(panel_pos, self.panel.surface.get_size())
        register_blocker(panel_rect)
        # Hover y selección
        for tool, btn in self.buttons.items():
            rect = self.icon_rects[tool]
            if btn.hover:
                hover_surf = pygame.Surface(rect.size, pygame.SRCALPHA)
                hover_surf.fill(self.hover_color)
                screen.blit(hover_surf, rect.topleft)
            if self.controller.is_active(tool):
                # Optional blinking when controller exposes blink_active(tool)
                blink = False
                try:
                    if hasattr(self.controller, 'blink_active') and callable(getattr(self.controller, 'blink_active')):
                        blink = bool(self.controller.blink_active(tool))
                except Exception:
                    blink = False
                if blink:
                    ticks = pygame.time.get_ticks()
                    phase_on = ((ticks // 300) % 2) == 0
                    if phase_on:
                        pygame.draw.rect(screen, self.selection_color, rect, self.selection_border_width)
                else:
                    pygame.draw.rect(screen, self.selection_color, rect, self.selection_border_width)

    def handle_event(self, event):
        """
        Delegar eventos de arrastre al panel.
        """
        header = pygame.Rect(self.panel.pos or (self.x, self.y), self.panel.surface.get_size())
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 3 and header.collidepoint(event.pos):
            logger.debug("[DEBUG][%s][DRAG START] pos=%s", self.name, event.pos)
        res = self.panel.handle_event(event, header)
        if event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 3:
            logger.debug("[DEBUG][%s][DRAG END] panel.pos=%s", self.name, self.panel.pos)
        return res
