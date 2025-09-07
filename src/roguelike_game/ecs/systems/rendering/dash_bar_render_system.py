import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite

class DashBarRenderSystem:
    """
    Renderiza la barra de cargas de dash del jugador por encima de la barra de vida.

    • Una unidad por carga total.
    • Cargas disponibles: segmento lleno (cian).
    • Carga en recarga (sequential): segmento siguiente con relleno parcial según progreso.
    • Ubicación: justo por encima de la health bar (unos píxeles más arriba).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Estilo
        self.bar_height = 4
        self.margin = 4  # separación por encima de la health bar
        self.bg_color = (40, 40, 40)
        self.fill_color = (40, 200, 255)
        self.recharge_color = (120, 220, 255)
        self.border_color = (0, 0, 0)
        self.segment_gap = 2

    def update(self, world, screen, camera):
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        comps = world.components
        meters = comps.get('DashMeterComponent', {})
        meter = meters.get(player_eid)
        if not meter:
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

        # Coordenadas sobre el sprite: por encima de la health bar (que usa 5px alto y 2px margin)
        screen_x, screen_y = camera.apply((pos.x, pos.y))
        # health bar y: screen_y - 2 - 5
        health_bar_h = 5
        health_bar_margin = 2
        base_y = screen_y - health_bar_margin - health_bar_h
        bar_y = base_y - self.margin - self.bar_height
        bar_x = screen_x + scaled_w / 2 - bar_width / 2

        total = max(1, int(meter.total))
        current = max(0, int(meter.current))
        # layout por segmentos
        # dejar pequeños gaps entre segmentos
        seg_w = (bar_width - self.segment_gap * (total - 1)) / total if total > 0 else bar_width

        # Dibujo por segmento
        for i in range(total):
            x0 = bar_x + i * (seg_w + self.segment_gap)
            rect = pygame.Rect(int(x0), int(bar_y), int(seg_w), int(self.bar_height))
            # fondo
            pygame.draw.rect(screen, self.bg_color, rect)
            # relleno completo si la carga está disponible
            if i < current:
                pygame.draw.rect(screen, self.fill_color, rect)
            # si es la siguiente en recarga, dibujar relleno parcial
            elif i == current and current < total and meter.policy == 'sequential':
                width = int(seg_w * max(0.0, min(1.0, meter.progress)))
                if width > 0:
                    part = pygame.Rect(rect.x, rect.y, width, rect.height)
                    pygame.draw.rect(screen, self.recharge_color, part)
            # borde
            pygame.draw.rect(screen, self.border_color, rect, 1)
