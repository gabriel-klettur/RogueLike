import pygame
from typing import Tuple
from roguelike_engine.console.console_model import ConsoleState


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
        # Scrollbar params
        self.scrollbar_width = 6
        self.scrollbar_margin = 2
        self.scrollbar_color = (200, 200, 200)
        self.scrollbar_bg = (80, 80, 80)

    def render(self, screen: pygame.Surface) -> None:
        """Dibuja la consola sobre el screen si está abierta."""
        if not self.state.is_open:
            return
        # Background
        surf = pygame.Surface((self.rect.width, self.rect.height), flags=pygame.SRCALPHA)
        surf.fill(self.bg_color)
        screen.blit(surf, (self.rect.x, self.rect.y))

        # Establecer clipping para no dibujar fuera
        prev_clip = screen.get_clip()
        screen.set_clip(self.rect)

        # Draw history lines con scrollback
        max_visible = max(1, self.rect.height // self.line_height - 1)  # dejar línea para prompt
        total = len(self.state.history)
        offset = self.state.history_scroll  # 0 = al final
        # Índices desde el final
        start_from_end = offset + max_visible
        start_index = max(0, total - start_from_end)
        end_index = max(0, total - offset)
        history_slice = self.state.history[start_index:end_index]

        y = self.rect.y
        x_text = self.rect.x + 5
        for line in history_slice:
            txt_surf = self.font.render(line, True, self.text_color)
            screen.blit(txt_surf, (x_text, y + 5))
            y += self.line_height

        # Draw prompt
        prompt = "> " + self.state.input_buffer
        txt_prompt = self.font.render(prompt, True, self.prompt_color)
        prompt_y = self.rect.y + self.rect.height - self.line_height - 5
        screen.blit(txt_prompt, (x_text, prompt_y))

        # Draw cursor (parpadeo ~2 Hz)
        cursor_x = self.font.size("> " + self.state.input_buffer[:self.state.cursor_pos])[0]
        cursor_pos = (x_text + cursor_x, prompt_y)
        try:
            ticks = pygame.time.get_ticks()
        except Exception:
            ticks = 0
        if (ticks // 500) % 2 == 0:
            pygame.draw.line(screen, self.prompt_color,
                             cursor_pos,
                             (cursor_pos[0], cursor_pos[1] + self.line_height), 2)

        # Scrollbar (si hay más líneas que visibles)
        if total > max_visible:
            track_x = self.rect.right - self.scrollbar_margin - self.scrollbar_width
            track_y = self.rect.y + self.scrollbar_margin
            track_h = self.rect.height - 2 * self.scrollbar_margin
            track_rect = pygame.Rect(track_x, track_y, self.scrollbar_width, track_h)
            pygame.draw.rect(screen, self.scrollbar_bg, track_rect)

            # Altura del pulgar proporcional
            thumb_h = max(10, int(track_h * (max_visible / total)))
            # Posición del pulgar: 0 (al fondo) cuando offset=0, arriba cuando offset grande
            # mapea offset [0, total-max_visible] -> y [track_y+track_h-thumb_h, track_y]
            max_offset = max(0, total - max_visible)
            if max_offset > 0:
                t = min(1.0, max(0.0, offset / max_offset))
            else:
                t = 0.0
            thumb_y = int(track_y + (track_h - thumb_h) * (1.0 - t))
            thumb_rect = pygame.Rect(track_x, thumb_y, self.scrollbar_width, thumb_h)
            pygame.draw.rect(screen, self.scrollbar_color, thumb_rect)

        # Restaurar clipping
        screen.set_clip(prev_clip)
