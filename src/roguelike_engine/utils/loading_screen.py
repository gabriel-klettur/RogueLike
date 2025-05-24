import os
import pygame
import roguelike_engine.config.config as config

class LoadingScreen:
    def __init__(self, screen: pygame.Surface, bg_filename: str | None = None, bg_color: tuple[int, int, int] = (0, 0, 0)):
        self.screen = screen
        self.w, self.h = screen.get_size()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        # Background
        if bg_filename:
            bg_path = os.path.join(config.ASSETS_DIR, bg_filename)
            try:
                bg_img = pygame.image.load(bg_path).convert()
                self.bg = pygame.transform.scale(bg_img, (self.w, self.h))
            except Exception as e:
                print(f"[Warning] No se pudo cargar {bg_path}: {e}")
                self.bg = None
                self.bg_color = bg_color
        else:
            self.bg = None
            self.bg_color = bg_color

        # Barra de progreso
        bar_width = int(self.w * 0.6)
        bar_height = 30
        bar_x = (self.w - bar_width) // 2
        bar_y = int(self.h * 0.8)
        self.bar_outer = pygame.Rect(bar_x, bar_y, bar_width, bar_height)
        self.bar_padding = 3

    def draw(self, progress: float, message: str):
        # Evitar que se cuelgue la ventana
        pygame.event.pump()
        # Dibujar fondo
        if self.bg:
            self.screen.blit(self.bg, (0, 0))
        else:
            self.screen.fill(self.bg_color)
        # Dibujar barra exterior
        pygame.draw.rect(self.screen, (255, 255, 255), self.bar_outer, 2)
        # Dibujar barra interior
        inner_width = int((self.bar_outer.width - 2 * self.bar_padding) * max(0, min(progress, 1)))
        inner_rect = pygame.Rect(
            self.bar_outer.x + self.bar_padding,
            self.bar_outer.y + self.bar_padding,
            inner_width,
            self.bar_outer.height - 2 * self.bar_padding
        )
        pygame.draw.rect(self.screen, (0, 200, 0), inner_rect)
        # Renderizar texto
        text_surf = self.font.render(message, True, (255, 255, 255))
        text_rect = text_surf.get_rect(center=(self.w // 2, self.bar_outer.y - 20))
        self.screen.blit(text_surf, text_rect)
        # Actualizar pantalla
        pygame.display.flip()
