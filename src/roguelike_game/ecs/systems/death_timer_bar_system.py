import pygame
import time
import roguelike_engine.config.config as config
from roguelike_game.ecs.components.death_timer import DeathTimer

class DeathTimerBarSystem:
    """
    Dibuja barras decrecientes de temporizador de muerte:
      - Se aplica a NPCs muertos (DeathTimer activo).
      - Sustituye la barra de salud cuando DEBUG=False.
      - Totalmente parametrizable y escalable.
    """
    def __init__(self, bar_height: int = 5, offset: int = 2,
                 color_bg=(50,50,50), color_fg=(255,0,0)):
        """
        Inicializa la configuración de la barra.

        Args:
            bar_height (int): Alto de la barra en píxeles.
            offset (int): Separación vertical respecto al nombre.
            color_bg (tuple): Color de fondo (RGB).
            color_fg (tuple): Color de primer plano (RGB).
        """
        self.bar_height = bar_height
        self.offset = offset
        self.color_bg = color_bg
        self.color_fg = color_fg

    def update(self, world, screen, camera):
        """
        Dibuja la barra de muerte para cada temporizador activo.

        Args:
            world: Mundo ECS con componentes.
            screen: Superficie de Pygame para dibujar.
            camera: Cámara para convertir coordenadas.
        """
        # Solo fuera de modo DEBUG para anular HealthBarSystem
        if config.DEBUG:
            return
        now = time.time()
        for eid, dt in self._active_timers(world, now).items():
            params = self._gather_draw_params(eid, world, now, dt, camera)
            if params:
                self._draw_bar(screen, **params)

    def _active_timers(self, world, now):
        """
        Filtra y devuelve DeathTimers no expirados.
        """
        return {
            eid: dt for eid, dt in world.components.get('DeathTimer', {}).items()
            if (now - dt.start_time) < dt.duration
        }

    def _gather_draw_params(self, eid, world, now, dt, camera):
        """
        Calcula x, y, width, height y ratio para dibujar la barra.

        Returns:
            dict: Parámetros de dibujo, o None si faltan datos.
        """
        pos = world.components['Position'].get(eid)
        sprite = world.components['Sprite'].get(eid)
        if not pos or not sprite:
            return None

        scale = world.components['Scale'].get(eid)
        base_w = sprite.image.get_width()
        width = int(base_w * (scale.scale if scale else 1))

        elapsed = now - dt.start_time
        ratio = max(0.0, (dt.duration - elapsed) / dt.duration)

        sx, sy = camera.apply((pos.x, pos.y))
        return {
            'x': sx,
            'y': int(sy - self.offset - self.bar_height),
            'width': width,
            'height': self.bar_height,
            'ratio': ratio
        }

    def _draw_bar(self, screen, x, y, width, height, ratio):
        """
        Dibuja el fondo y el primer plano de la barra.
        """
        bg = pygame.Rect(x, y, width, height)
        fg = pygame.Rect(x, y, int(width * ratio), height)
        pygame.draw.rect(screen, self.color_bg, bg)
        pygame.draw.rect(screen, self.color_fg, fg)
