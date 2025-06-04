import pygame
import time
import roguelike_engine.config.config as config
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_engine.utils.benchmark import benchmark

class DeathTimerBarSystem:
    """
    Dibuja barras decrecientes para NPCs muertos:
      - Se aplica mientras el componente DeathTimer esté activo.
      - Sólo se muestra en modo de juego (DEBUG=False).
      - Reemplaza a la barra de salud para indicar tiempo restante de cadáver.
    """

    def __init__(self, perf_log,
                 bar_height: int = 5,
                 offset: int = 2,
                 color_bg: tuple = (50, 50, 50),
                 color_fg: tuple = (255, 0, 0)):
        """
        Inicializa la configuración de la barra de temporizador.

        Args:
            bar_height (int): Alto de la barra en píxeles.
            offset (int): Separación vertical respecto a la parte superior del sprite.
            color_bg (tuple): Color de fondo (RGB).
            color_fg (tuple): Color de primer plano (RGB), indica tiempo restante.
        """
        # Altura fija de la barra
        self.bar_height = bar_height
        # Espacio entre sprite y la barra
        self.offset = offset
        # Colores para fondo y frente
        self.color_bg = color_bg
        self.color_fg = color_fg
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.DeathTimerBarSystem.update")
    def update(self, world, screen, camera):
        """
        Recorre todos los DeathTimers activos y dibuja su barra.

        - Omite dibujo si DEBUG=True para no interferir con DebugSystem.
        - Calcula proporción de tiempo restante y despliega la barra correspondiente.
        """
        # Sólo dibujar en modo de juego normal
        if config.DEBUG:
            return

        now = time.time()

        # 1) Filtrar temporizadores activos
        active = self._active_timers(world, now)

        # 2) Para cada temporizador, obtener parámetros de dibujo
        for eid, dt in active.items():
            # Sólo renderizar para entidades en Estado de Muerte
            state_comp = world.components.get('NPCState', {}).get(eid)
            if not state_comp or not isinstance(state_comp.fsm.current_state, DeathState):
                continue
            params = self._gather_draw_params(eid, world, now, dt, camera)
            if params:
                # 3) Dibujar la barra usando los parámetros calculados
                self._draw_bar(screen, **params)

    def _active_timers(self, world, now):
        """
        Filtra y devuelve los componentes DeathTimer que aún no expiraron.

        Args:
            world: Instancia del world ECS.
            now (float): Tiempo actual en segundos.
        Returns:
            dict[eid, DeathTimer]: Temporizadores con tiempo restante > 0.
        """
        return {
            eid: timer
            for eid, timer in world.components.get('DeathTimer', {}).items()
            if (now - timer.start_time) < timer.duration
        }

    def _gather_draw_params(self, eid, world, now, dt, camera):
        """
        Calcula las coordenadas y dimensiones de la barra de muerte.

        - Ajusta su ancho según el tamaño del sprite y su escala.
        - Calcula la proporción de tiempo restante para la longitud del primer plano.

        Args:
            eid: ID de la entidad.
            world: Mundo ECS.
            now (float): Tiempo actual.
            dt: Componente DeathTimer de la entidad.
            camera: Cámara para conversión de coordenadas.

        Returns:
            dict o None: Parámetros {'x','y','width','height','ratio'} o None si faltan datos.
        """
        # Obtener posición y sprite de la entidad
        pos = world.components['Position'].get(eid)
        sprite = world.components['Sprite'].get(eid)
        if not pos or not sprite:
            # No podemos dibujar sin posición o sprite
            return None

        # Ajuste de escala si existe componente Scale
        scale_comp = world.components['Scale'].get(eid)
        scale = scale_comp.scale if scale_comp else 1.0

        # Anchura base del sprite en píxeles
        base_w = sprite.image.get_width()
        # Anchura final de la barra
        width = int(base_w * scale)

        # Tiempo transcurrido desde el inicio del temporizador
        elapsed = now - dt.start_time
        # Proporción restante [0.0, 1.0]
        ratio = max(0.0, (dt.duration - elapsed) / dt.duration)

        # Convertir posición del mundo a coordenadas de pantalla
        sx, sy = camera.apply((pos.x, pos.y))

        return {
            'x': sx,
            # Colocar la barra justo encima del sprite, con offset
            'y': int(sy - self.offset - self.bar_height),
            'width': width,
            'height': self.bar_height,
            'ratio': ratio
        }

    def _draw_bar(self, screen, x, y, width, height, ratio):
        """
        Dibuja la barra de fondo y la porción de tiempo restante.

        Args:
            screen: Superficie de Pygame donde dibujar.
            x, y (int): Coordenadas de la esquina superior izquierda.
            width (int): Anchura total de la barra.
            height (int): Altura de la barra.
            ratio (float): Proporción de primer plano (0.0 a 1.0).
        """
        # Fondo (barra completa)
        bg_rect = pygame.Rect(x, y, width, height)
        # Frente (barra proporcional al tiempo restante)
        fg_rect = pygame.Rect(x, y, int(width * ratio), height)

        # Dibujar primero el fondo y luego el frente
        pygame.draw.rect(screen, self.color_bg, bg_rect)
        pygame.draw.rect(screen, self.color_fg, fg_rect)
