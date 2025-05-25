import pygame
import time
import roguelike_engine.config.config as config
from roguelike_game.ecs.components.death_timer import DeathTimer

class DeathTimerBarSystem:
    """
    Dibuja una barra decreciente del temporizador de muerte sobre NPCs muertos en modo DEBUG.
    """
    def __init__(self, bar_height: int = 5, offset: int = 2, color_bg=(0,0,0), color_fg=(255,0,0)):
        self.bar_height = bar_height
        self.offset = offset
        self.color_bg = color_bg
        self.color_fg = color_fg

    def update(self, world, screen, camera):
        # Mostrar solo cuando no estamos en modo DEBUG
        if config.DEBUG:
            return
        now = time.time()
        dt_store = world.components.get('DeathTimer', {})
        for eid, dt in dt_store.items():
            remaining = dt.duration - (now - dt.start_time)
            if remaining <= 0:
                continue
            # Obtener posición y sprite
            pos = world.components['Position'].get(eid)
            sprite = world.components['Sprite'].get(eid)
            if not pos or not sprite:
                continue
            # Calcular ancho de la barra
            scale_comp = world.components['Scale'].get(eid)
            base_w = sprite.image.get_width()
            width = int(base_w * (scale_comp.scale if scale_comp else 1))
            # Proporción restante
            ratio = max(0.0, remaining / dt.duration)
            # Coordenadas en pantalla del top-left del sprite
            sx, sy = camera.apply((pos.x, pos.y))
            # Posicionar barra justo debajo del nombre (igual que la de salud)
            x = sx
            y = sy - self.offset - self.bar_height
            # Rectángulos
            bg_rect = pygame.Rect(x, y, width, self.bar_height)
            fg_rect = pygame.Rect(x, y, int(width * ratio), self.bar_height)
            # Dibujar fondo y primer plano
            pygame.draw.rect(screen, self.color_bg, bg_rect)
            pygame.draw.rect(screen, self.color_fg, fg_rect)
