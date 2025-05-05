# Path: src/roguelike_game/entities/player/view/hud_view.py

import time
import pygame

class HUDView:
    def __init__(self, icons: dict[str, pygame.Surface]):
        self.icons = icons
        self._scaled_icons: dict[int, dict[str, pygame.Surface]] = {}
        self._cached_fonts: dict[int, pygame.font.Font] = {}

    def get_font(self, size):
        if size not in self._cached_fonts:
            self._cached_fonts[size] = pygame.font.SysFont('Arial', size)
        return self._cached_fonts[size]

    def draw_status_bars(self, player_model, screen, camera):
        """
        Dibuja las barras de vida/mana/energía centradas sobre el jugador.
        """
        stats = player_model.stats

        # Dimensiones de cada barra
        bar_w = int(60 * camera.zoom)
        bar_h = int(20 * camera.zoom)
        spacing = int(2 * camera.zoom)

        # Calcular cuánto ocupa verticalmente el bloque de 3 barras
        total_bars_h = bar_h * 3 + spacing * 2

        # Centro del jugador en coordenadas de mundo
        center_world = (
            player_model.x + player_model.sprite_size[0] / 2,
            player_model.y + player_model.sprite_size[1] / 2
        )
        # Convertir al punto en pantalla
        center_screen = camera.apply(center_world)

        # Medida en pantalla de mitad de la altura del sprite
        _, half_h_screen = camera.scale((0, player_model.sprite_size[1] / 2))

        # Punto de partida para las barras: justo encima de la cabeza
        start_x = int(center_screen[0] - bar_w / 2)
        start_y = int(center_screen[1] - half_h_screen - total_bars_h)

        font = self.get_font(int(12 * camera.zoom))

        def draw_bar(curr, maxi, color, y_offset):
            x = start_x
            y = start_y + y_offset
            # Fondo
            pygame.draw.rect(screen, (40, 40, 40), (x, y, bar_w, bar_h))
            # Relleno
            fill = int(bar_w * (curr / maxi))
            pygame.draw.rect(screen, color, (x, y, fill, bar_h))
            # Borde
            pygame.draw.rect(screen, (0, 0, 0), (x, y, bar_w, bar_h), 1)
            # Texto centrado
            txt = font.render(f"{int(curr)}/{int(maxi)}", True, (255, 255, 255))
            txt_rect = txt.get_rect(center=(x + bar_w // 2, y + bar_h // 2))
            screen.blit(txt, txt_rect)

        # Dibujar las tres barras con su separación
        draw_bar(stats.health, stats.max_health, (0, 255, 0), 0)
        draw_bar(stats.mana,   stats.max_mana,   (0, 128, 255), bar_h + spacing)
        draw_bar(stats.energy, stats.max_energy, (255, 50, 50),  2 * (bar_h + spacing))

    def render_cooldowns(self, player_model, screen):
        """
        Dibuja los iconos de cooldown debajo de la pantalla; no los tocamos aquí.
        """
        stats = player_model.stats
        now = time.time()
        font = self.get_font(16)

        size = 48
        spacing = 60
        total = len(self.icons) * spacing
        start_x = (screen.get_width() - total) // 2
        start_y = screen.get_height() - 90

        for i, (name, icon) in enumerate(self.icons.items()):
            if size not in self._scaled_icons:
                self._scaled_icons[size] = {}
            if name not in self._scaled_icons[size]:
                self._scaled_icons[size][name] = pygame.transform.scale(icon, (size, size))
            ico = self._scaled_icons[size][name]
            ix = start_x + i * spacing
            iy = start_y
            screen.blit(ico, (ix, iy))

            # Cooldowns
            last_attr = f"last_{name.replace(' ', '_').lower()}_time"
            cd_attr   = f"{name.replace(' ', '_').lower()}_cooldown"
            last = getattr(stats, last_attr, None)
            cd   = getattr(stats, cd_attr, None)
            if last is not None and cd is not None:
                elapsed = now - last
                if elapsed < cd:
                    ratio = 1 - elapsed / cd
                    h = int(size * ratio)
                    overlay = pygame.Surface((size, h), pygame.SRCALPHA)
                    overlay.fill((0, 0, 0, 180))
                    screen.blit(overlay, (ix, iy + (size - h)))
                    rem = int(cd - elapsed) + 1
                    txt = font.render(str(rem), True, (255, 255, 255))
                    screen.blit(txt, txt.get_rect(center=(ix + size // 2, iy + size // 2)))
