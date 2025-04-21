import pygame

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
        pygame.draw.rect(screen, color, rect, 4)