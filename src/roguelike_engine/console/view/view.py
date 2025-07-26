import pygame
from typing import Tuple
from roguelike_engine.console.model.model import ConsoleState


class ConsoleView:
    """
    Vista de la consola: dibuja overlay, historial y prompt.
    """
    def __init__(self,
                 state: ConsoleState,
                 rect: pygame.Rect,
                 font: pygame.font.Font = None,
                 bg_color: Tuple[int, int, int, int] = (0, 0, 0, 150),
                 text_color: Tuple[int, int, int] = (255, 255, 255),
                 prompt_color: Tuple[int, int, int] = (0, 255, 0)):
        self.state = state
        self.rect = rect
        self.bg_color = bg_color
        self.text_color = text_color
        self.prompt_color = prompt_color
        self.font = font or pygame.font.SysFont(None, 20)
        # Precompute line height
        self.line_height = self.font.get_height()

    def render(self, screen: pygame.Surface) -> None:
        """Dibuja la consola sobre el screen si está abierta."""
        if not self.state.is_open:
            return
        # Background
        surf = pygame.Surface((self.rect.width, self.rect.height), flags=pygame.SRCALPHA)
        surf.fill(self.bg_color)
        screen.blit(surf, (self.rect.x, self.rect.y))

        # Draw history lines
        max_visible = self.rect.height // self.line_height - 1  # dejar línea para prompt
        history = self.state.history[-max_visible:]
        y = self.rect.y
        for line in history:
            txt_surf = self.font.render(line, True, self.text_color)
            screen.blit(txt_surf, (self.rect.x + 5, y + 5))
            y += self.line_height

        # Draw prompt
        prompt = "> " + self.state.input_buffer
        txt_prompt = self.font.render(prompt, True, self.prompt_color)
        screen.blit(txt_prompt, (self.rect.x + 5, self.rect.y + self.rect.height - self.line_height - 5))

        # Draw cursor
        cursor_x = self.font.size("> " + self.state.input_buffer[:self.state.cursor_pos])[0]
        cursor_pos = (self.rect.x + 5 + cursor_x,
                      self.rect.y + self.rect.height - self.line_height - 5)
        # Parpadeo simple: siempre mostrar
        pygame.draw.line(screen, self.prompt_color,
                         cursor_pos,
                         (cursor_pos[0], cursor_pos[1] + self.line_height), 2)
