import pygame
import time
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
from roguelike_engine.utils.benchmark import benchmark

"""
Módulo que provee un sistema de debug para mostrar contadores de muerte
(tiempo restante antes de eliminar la entidad) sobre los NPCs.
"""

class DeathTimerDebugSystem:
    """
    Sistema que renderiza un contador de segundos sobre NPCs muertos,
    mostrando cuánto tiempo falta para que sean removidos.
    """
    def __init__(self, font_size: int = 32, color: tuple = (255, 0, 0), perf_log=None):
        """
        Inicializa el sistema de debug.

        Parámetros:
        - font_size: tamaño de la fuente para el texto de cuenta regresiva.
        - color: tupla RGB para el color del texto.
        """
        # Asegura que el módulo de fuentes de pygame esté inicializado
        if not pygame.font.get_init():
            pygame.font.init()
        # Crear la fuente con el tamaño indicado
        self.font = pygame.font.SysFont(None, font_size)
        self.color = color
        # Pre-cache de superficies de texto para valores de 0 a 60 segundos
        self.text_cache = {i: self.font.render(str(i), True, self.color) for i in range(0, 61)}
        # Guardar perf_log para benchmark
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.DeathTimerDebugSystem.update")
    def update(self, world, screen, camera):
        """
        Dibuja en pantalla los contadores sobre cada entidad con DeathTimer activo.

        Pasos:
        1. Verificar que estemos en modo DEBUG_ENTITIES.
        2. Obtener el tiempo actual.
        3. Para cada entidad con componente DeathTimer:
           - Calcular segundos restantes.
           - Saltar si ya expiró.
           - Obtener posición y sprite para calcular dónde dibujar.
           - Ajustar por escalado si existe.
           - Obtener (o crear) la superficie de texto para el número.
           - Calcular coordenadas en pantalla y renderizar.
        """
        # Renderizar contador de muerte cada frame en la superficie dada
        now = time.time()
        # Obtener diccionario de componentes DeathTimer, o uno vacío
        dt_store = world.components.get('DeathTimer', {})

        for eid, dt in dt_store.items():
            # Sólo renderizar para entidades en Estado de Muerte
            state_comp = world.components.get('NPCState', {}).get(eid)
            if not state_comp or not isinstance(state_comp.fsm.current_state, DeathState):
                continue
            # Tiempo restante en segundos (entero)
            remaining = int(dt.duration - (now - dt.start_time))
            if remaining <= 0:
                # Ya no mostrar si el timer expiró
                continue

            # Obtener posición; salta si no existe
            pos = world.components['Position'].get(eid)
            if not pos:
                continue

            # Obtener sprite para medir dimensiones
            sprite = world.components['Sprite'].get(eid)
            # Altura de sprite, ajustada por escala si aplica
            disp_h = sprite.image.get_height()
            scale_comp = world.components['Scale'].get(eid)
            if scale_comp:
                disp_h = int(disp_h * scale_comp.scale)

            # Conseguir o renderizar texto para el número restante
            text_surf = self.text_cache.get(remaining)
            if text_surf is None:
                text_surf = self.font.render(str(remaining), True, self.color)
                self.text_cache[remaining] = text_surf
            tw, th = text_surf.get_size()

            # Convertir posición del mundo a pantalla
            sx, sy = camera.apply((pos.x, pos.y))
            # Calcular posición X centrado sobre el sprite
            sprite_w = sprite.image.get_width() * (scale_comp.scale if scale_comp else 1)
            tx = sx + (sprite_w - tw) // 2
            # Ubicar el texto encima del sprite, con un pequeño margen y offset vertical
            ty = sy - disp_h - th - 5 + 100

            # Renderizar la superficie de texto en pantalla
            screen.blit(text_surf, (int(tx), int(ty)))