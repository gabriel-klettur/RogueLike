# roguelike_project/systems/editor/tiles/toolbar.py

import pygame
from roguelike_project.utils.loader import load_image

class TileToolbar:
    """
    Barra de herramientas para el TileEditor:
      - select
      - brush
      - eyedropper
    """
    TOOLS = ["select", "brush", "eyedropper"]
    ICON_PATHS = {
        "select":        "assets/ui/select_tool.png",
        "brush":         "assets/ui/brush_tool.png",
        "eyedropper":    "assets/ui/eyedropper_tool.png",
    }

    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

        # Cargar iconos (64×64)
        self.icons = {
            tool: load_image(path, (64, 64))
            for tool, path in self.ICON_PATHS.items()
        }

        # Layout
        self.x = 10
        self.y = 10
        self.size = 64
        self.padding = 8

        # Rects para detectar clicks
        self.icon_rects: dict[str, pygame.Rect] = {}

    def render(self, screen):
        """Dibuja la toolbar en pantalla."""
        for idx, tool in enumerate(self.TOOLS):
            px = self.x
            py = self.y + idx * (self.size + self.padding)
            rect = pygame.Rect(px, py, self.size, self.size)
            self.icon_rects[tool] = rect

            # icono
            screen.blit(self.icons[tool], (px, py))

            # borde: resaltado si está activa
            color = (255, 200, 0) if self.editor.current_tool == tool else (255, 255, 255)
            pygame.draw.rect(screen, color, rect, 2)

    def handle_click(self, mouse_pos) -> bool:
        """
        Si el click cae sobre un icono, cambia la herramienta
        y devuelve True para consumir el evento.
        """
        for tool, rect in self.icon_rects.items():
            if rect.collidepoint(mouse_pos):
                self.editor.current_tool = tool
                return True
        return False
