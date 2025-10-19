import time
import math
import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.combat.mana import Mana


class ManaBarRenderSystem:
    """
    Renderiza la barra de maná del jugador por encima de las barras existentes
    (Dash y Salud), centrada sobre el sprite.

    • Ancho: igual al ancho del sprite escalado.
    • Altura: 4 px.
    • Color de relleno: azul.
    • Se dibuja solo si existe el componente Mana para el jugador.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Estilo
        self.bar_height = 4
        self.margin = 6  # separación por encima de la barra de dash
        self.bg_color = (40, 40, 40)
        self.fill_color = (80, 120, 255)
        self.border_color = (0, 0, 0)

    def update(self, world, screen, camera):
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        comps = world.components
        mana: Mana = comps.get('Mana', {}).get(player_eid)
        if not mana:
            return
        pos: Position = comps['Position'].get(player_eid)
        spr: Sprite = comps['Sprite'].get(player_eid)
        if not pos or not spr:
            return
        scale: Scale = comps.get('Scale', {}).get(player_eid)
        entity_scale = scale.scale if scale else 1.0
        orig_w, orig_h = spr.image.get_size()
        scaled_w = int(orig_w * entity_scale)
        bar_width = scaled_w

        # Coordenadas: por encima de la barra de dash (que está por encima de la de vida)
        screen_x, screen_y = camera.apply((pos.x, pos.y))
        # health bar: y = screen_y - 2 - 5
        health_bar_h = 5
        health_bar_margin = 2
        dash_bar_h = 4
        dash_margin = 4
        base_y = screen_y - health_bar_margin - health_bar_h
        dash_y = base_y - dash_margin - dash_bar_h
        bar_y = dash_y - self.margin - self.bar_height
        bar_x = screen_x + scaled_w / 2 - bar_width / 2

        # Proporción de maná
        max_mana = max(1, int(getattr(mana, 'max_mana', 0) or 0))
        current = max(0, int(getattr(mana, 'current_mana', 0) or 0))
        ratio = max(0.0, min(1.0, float(current) / float(max_mana)))
        fill_width = int(bar_width * ratio)

        # Colores (tinte amarillo si godmode)
        godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False))
        fill_color = (255, 230, 100) if godmode else self.fill_color
        # Fondo
        pygame.draw.rect(screen, self.bg_color, (bar_x, bar_y, bar_width, self.bar_height))
        # Relleno
        if fill_width > 0:
            pygame.draw.rect(screen, fill_color, (bar_x, bar_y, fill_width, self.bar_height))
        # Borde
        pygame.draw.rect(screen, self.border_color, (bar_x, bar_y, bar_width, self.bar_height), 1)

        # Flash azul cuando no alcanza el maná o cuando está vacío
        try:
            now = time.time()
            flash_until = getattr(world, '_mana_flash_until', {}).get(player_eid)
            active_flash = flash_until is not None and now < float(flash_until)
        except Exception:
            active_flash = False
        zero_flash = (current <= 0)
        if active_flash or zero_flash:
            # Pulso senoidal para alpha
            t = pygame.time.get_ticks() / 1000.0
            alpha = int(120 + 80 * (0.5 + 0.5 * math.sin(t * 10)))
            overlay = pygame.Surface((int(bar_width), int(self.bar_height)), pygame.SRCALPHA)
            # Azul brillante
            overlay.fill((100, 160, 255, max(60, min(220, alpha))))
            screen.blit(overlay, (int(bar_x), int(bar_y)))
