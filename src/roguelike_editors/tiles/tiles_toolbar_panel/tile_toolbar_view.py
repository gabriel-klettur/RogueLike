import pygame
from roguelike_editors.tiles.tiles_editor_config import TOOLS, BTN_W, BTN_H, THUMB, PAD, CLR_SELECTION, CLR_HOVER
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.button import Button


class TileToolbarView:
    """
    Vista de la barra de herramientas de tiles.

    Separa responsabilidades de cálculo de posición, renderizado de iconos,
    aplicación de hover y resaltado de herramienta activa.
    """
    def __init__(self, toolbar):
        """
        Args:
            toolbar: Controlador asociado que provee estado y assets.
        """
        self.toolbar = toolbar
        # Inicializar panel y botones usando roguelike_ui
        size = self.toolbar.size
        padding = self.toolbar.padding
        width = size
        height = len(TOOLS) * (size + padding) - padding
        self.panel = DraggablePanel(width, height)
        self.panel.pos = (self.toolbar.x, self.toolbar.y)
        # Crear botones para cada herramienta
        self.buttons = {
            tool: Button(
                pygame.Rect(0, idx * (size + padding), size, size),
                bgcolor=(0, 0, 0, 0),
                border_color=(255, 255, 255),
                hover_color=(255, 255, 0, 100)
            )
            for idx, tool in enumerate(TOOLS)
        }

    def render(self, screen):
        """
        Dibuja la toolbar en pantalla, con soporte para:
        - Posicionamiento arrastrable.
        - Íconos de herramientas.
        - Overlay de hover.
        - Borde de selección para herramienta activa.

        Args:
            screen: Superficie de pygame donde dibujar.
        """
        mouse_pos = pygame.mouse.get_pos()
        panel = self.panel
        # Calcular dimensiones y redimensionar panel
        size = self.toolbar.size
        padding = self.toolbar.padding
        width = size
        height = len(TOOLS) * (size + padding) - padding
        panel.resize(width, height)
        panel_pos = panel.pos or (self.toolbar.x, self.toolbar.y)
        # Dibujar fondo de panel (se blitea tras renderizar botones e iconos)
        # screen.blit(panel.surface, panel_pos)
        # Coordenadas relativas del ratón para hover
        rel_mouse = (mouse_pos[0] - panel_pos[0], mouse_pos[1] - panel_pos[1])
        # Dibujar botones e iconos
        for tool, btn in self.buttons.items():
            idx = list(self.buttons.keys()).index(tool)
            btn.rect.topleft = (0, idx * (size + padding))
            btn.is_hovered(rel_mouse)
            btn.draw(panel.surface)
            # Icono centrado
            icon_surf = self.toolbar.icons[tool]
            icon_pos = (
                btn.rect.x + (size - icon_surf.get_width()) // 2,
                btn.rect.y + (size - icon_surf.get_height()) // 2
            )
            panel.surface.blit(icon_surf, icon_pos)
            # Guardar rect global para click
            global_rect = btn.rect.move(panel_pos)
            self.toolbar.icon_rects[tool] = global_rect


        # Blitear panel con botones e iconos en el lienzo
        screen.blit(panel.surface, panel_pos)
        # Hover y borde de selección sobre los iconos
        for tool, btn in self.buttons.items():
            rect = self.toolbar.icon_rects[tool]
            # Hover amarillo
            if btn.hover:
                self._draw_hover(screen, rect, mouse_pos)
            # Borde selección amarilla
            self._draw_selection_border(screen, tool, rect)

    def _get_panel_position(self) -> tuple[int, int]:
        """
        Obtiene la posición actual de la toolbar, priorizando
        el estado draggable y fallback a coordenadas por defecto.

        Returns:
            Tupla (x0, y0) de la posición superior izquierda.
        """
        ts = self.toolbar.editor_state.toolbar_state
        if ts.pos is not None:
            return ts.pos
        return self.toolbar.x, self.toolbar.y

    def _compute_icon_rect(self, x0: int, y0: int, idx: int) -> pygame.Rect:
        """
        Calcula el rectángulo de colisión y dibujo para el icono en la posición idx.
        """
        size = self.toolbar.size
        padding = self.toolbar.padding
        px = x0
        py = y0 + idx * (size + padding)
        return pygame.Rect(px, py, size, size)

    def _draw_icon(self, screen, tool: str, rect: pygame.Rect):
        """
        Renderiza la imagen del icono de la herramienta.
        """
        screen.blit(self.toolbar.icons[tool], rect.topleft)

    def _draw_hover(self, screen, rect: pygame.Rect, mouse_pos: tuple[int,int]):
        """
        Dibuja un overlay semitransparente al pasar el ratón sobre un icono.
        """
        if rect.collidepoint(mouse_pos):
            hover = pygame.Surface(rect.size, pygame.SRCALPHA)
            hover.fill((255, 255, 0, 100))
            screen.blit(hover, rect.topleft)

    def _draw_selection_border(self, screen, tool: str, rect: pygame.Rect):
        """
        Dibuja un borde amarillo si la herramienta está activa o tiene estado toggled.
        """
        state = self.toolbar.editor_state.toolbar_state
        current = self.toolbar.editor_state.current_tool

        # Determina si debe resaltarse según la herramienta
        if tool == "view":
            active = state.view_active
        elif tool == "view_layers":
            active = state.layers_view_open
        elif tool == "view_collisions":
            active = state.show_collisions or state.show_collisions_overlay
        elif tool == "brush":
            active = (current == "brush" and not (state.show_collisions or state.show_collisions_overlay))
        else:
            active = (current == tool)

        color = CLR_SELECTION if active else (255, 255, 255)
        pygame.draw.rect(screen, color, rect, 4)
