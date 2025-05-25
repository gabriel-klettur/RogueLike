import pygame
import time
import roguelike_engine.config.config as config
from roguelike_game.ecs.components.death_timer import DeathTimer

class DeathTimerDebugSystem:
    """
    Muestra en modo DEBUG un contador de segundos para que el NPC muerto desaparezca.
    """
    def __init__(self, font_size: int = 32, color: tuple = (255, 0, 0)):
        # Inicializar fuente
        if not pygame.font.get_init():
            pygame.font.init()
        self.font = pygame.font.SysFont(None, font_size)
        self.color = color
        # Pre-cache de superficies de texto para cada segundo de 0 a 60
        self.text_cache = {i: self.font.render(str(i), True, self.color) for i in range(0, 61)}

    def update(self, world, screen, camera):
        if not config.DEBUG:
            return
        now = time.time()
        dt_store = world.components.get('DeathTimer', {})
        for eid, dt in dt_store.items():
            remaining = int(dt.duration - (now - dt.start_time))
            if remaining <= 0:
                continue
            # Posición y dimensiones del NPC
            pos = world.components['Position'].get(eid)
            if not pos:
                continue
            sprite = world.components['Sprite'].get(eid)
            # Altura mostrada del sprite (con escala si existe)
            disp_h = sprite.image.get_height()
            scale_comp = world.components['Scale'].get(eid)
            if scale_comp:
                disp_h = int(disp_h * scale_comp.scale)
            # Obtener superficie caché de texto
            text_surf = self.text_cache.get(remaining)
            if text_surf is None:
                text_surf = self.font.render(str(remaining), True, self.color)
                self.text_cache[remaining] = text_surf
            tw, th = text_surf.get_size()
            # Coordenadas en pantalla del top-left del sprite
            sx, sy = camera.apply((pos.x, pos.y))
            # Dibujar justo encima del sprite, centrado horizontalmente
            tx = sx + (sprite.image.get_width() * (scale_comp.scale if scale_comp else 1) - tw) // 2
            # Mover el contador 100px más abajo
            ty = sy - disp_h - th - 5 + 100
            screen.blit(text_surf, (int(tx), int(ty)))
