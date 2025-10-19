import time
import pygame
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent


class ComboBarRenderSystem:
    """
    Renderiza una barra de combo (ventana de tiempo) y el contador actual sobre el jugador.

    • Solo visible cuando el combo está activo (current>0 y no ha expirado).
    • La barra muestra el tiempo restante para mantener el combo (se vacía hacia 0).
    • Muestra el número de hits del combo actual y un pequeño récord (best) opcional.
    • Se ubica por encima de la barra de Dash (que ya está por encima de la de vida).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 18)
        # Estilo (HUD)
        self.bar_height = 6
        self.margin_above_xp = 6
        self.bg_color = (40, 40, 40)
        self.fill_color = (255, 210, 40)  # dorado
        self.border_color = (0, 0, 0)
        self.text_color = (255, 255, 255)

    def update(self, world, screen, camera):
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        comps = world.components
        counters = comps.get('ComboCounterComponent', {})
        counter: ComboCounterComponent = counters.get(player_eid)
        if not counter:
            return
        now = time.time()
        if not counter.is_active(now):
            return
        # HUD: encima de la barra de experiencia y con el mismo ancho (mitad de pantalla, centrado)
        screen_w, screen_h = screen.get_size()
        xp_margin = 20
        xp_bar_h = 10
        xp_bar_w = int(screen_w * 0.5)
        xp_x = (screen_w - xp_bar_w) // 2
        xp_y = screen_h - xp_bar_h - xp_margin
        bar_width = xp_bar_w
        bar_x = xp_x
        bar_y = xp_y - self.margin_above_xp - self.bar_height
        # Progreso inverso: tiempo restante / ventana
        remaining = max(0.0, counter.window_end_time - now)
        total = counter.last_window_duration if counter.last_window_duration > 0 else float(counter.window_s)
        total = max(0.001, float(total))
        ratio = max(0.0, min(1.0, remaining / total))
        # Fondo
        pygame.draw.rect(screen, self.bg_color, (bar_x, bar_y, bar_width, self.bar_height))
        # Relleno
        fill_w = int(bar_width * ratio)
        if fill_w > 0:
            pygame.draw.rect(screen, self.fill_color, (bar_x, bar_y, fill_w, self.bar_height))
        # Borde
        pygame.draw.rect(screen, self.border_color, (bar_x, bar_y, bar_width, self.bar_height), 1)
        # Texto de combo
        txt = f"x{counter.current}"
        surf = self.font.render(txt, True, self.text_color)
        rect = surf.get_rect()
        # Colocar el contador a la derecha de la barra
        rect.midleft = (bar_x + bar_width + 10, bar_y + self.bar_height // 2)
        screen.blit(surf, rect)
        # Texto de combos (basado en muertes dentro del combo activo)
        done_txt = f"combo enemies in a row {counter.kill_combo_current}"
        done_surf = self.font.render(done_txt, True, self.text_color)
        done_rect = done_surf.get_rect()
        done_rect.bottomleft = (bar_x, bar_y - 2)
        screen.blit(done_surf, done_rect)
        # Flash/Fade al romper combo
        if now < float(counter.break_flash_end_time):
            dur = float(getattr(counter, 'break_flash_duration_s', 0.3))
            remaining_flash = max(0.0, counter.break_flash_end_time - now)
            alpha_ratio = 0.0 if dur <= 0 else min(1.0, remaining_flash / dur)
            alpha = int(255 * alpha_ratio)
            if alpha > 0:
                overlay = pygame.Surface((bar_width, self.bar_height), pygame.SRCALPHA)
                overlay.fill((255, 255, 255, alpha))
                screen.blit(overlay, (bar_x, bar_y))
