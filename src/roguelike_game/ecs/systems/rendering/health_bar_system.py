import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite


class HealthBarSystem:
    """
    Sistema para renderizar barras de salud sobre entidades vivas.

    • Se dibuja solo para entidades con componentes Position y Health.
    • Se omiten las entidades con un DeathTimer activo (muertas).
    • La barra se centra sobre el sprite, ajustándose a la escala.
    • Se segmenta cada 20 puntos de vida para referencia visual.
    """

    def __init__(self):
        """
        Inicializa el sistema de barras de salud.
        No mantiene estado interno.
        """
        pass

    def update(self, world, screen, camera, perf_log=None):
        """
        Recorre todas las entidades vivas y dibuja su barra de salud.

        Args:
            world: Instancia del ECS que contiene componentes y entidades.
            screen: Superficie de Pygame donde dibujar.
            camera: Cámara para convertir coordenadas de mundo a pantalla.
        """
        # 1) Obtener temporizadores activos para omitir entidades muertas
        death_timers = world.components.get('DeathTimer', {})

        # 2) Iterar entidades con Position y Health
        for eid in world.get_entities_with('Position', 'Health'):
            # 2.1) Si la entidad está “muerta”, saltarla
            if eid in death_timers:
                continue

            # 3) Obtener componentes necesarios
            pos: Position = world.components['Position'][eid]
            health: Health = world.components['Health'][eid]
            sprite: Sprite = world.components['Sprite'][eid]
            scale_comp: Scale = world.components['Scale'].get(eid)

            # 4) Calcular ancho de la barra basado en el ancho del sprite y su escala
            base_width = sprite.image.get_width()
            if scale_comp and scale_comp.scale != 1.0:
                base_width = int(base_width * scale_comp.scale)
            bar_width = base_width
            bar_height = 5
            margin = 2

            # 5) Obtener la posición del centro del collider de los pies
            multi = world.components.get('MultiCollider', {}).get(eid)
            if multi and 'feet' in multi.colliders:
                feet = multi.colliders['feet']
                foot_cx = pos.x + feet.offset_x + feet.width / 2
                foot_cy = pos.y + feet.offset_y + feet.height / 2
                screen_cx, screen_cy = camera.apply((foot_cx, foot_cy))
            else:
                # fallback: centro superior del sprite
                w, _ = sprite.image.get_size()
                screen_cx, screen_cy = camera.apply((pos.x + w/2, pos.y))

            # 6) Posición de la barra centrada horizontalmente sobre el pie
            bar_x = screen_cx - bar_width / 2
            bar_y = screen_cy - margin - bar_height

            # 7) Calcular proporción de vida restante y ancho de relleno
            ratio = max(0, health.current_hp) / health.max_hp
            fill_width = int(bar_width * ratio)

            # 8) Dibujar la barra de fondo (gris)
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, bar_width, bar_height))

            # 9) Dibujar el relleno de la barra (verde)
            pygame.draw.rect(screen, (0, 255, 0), (bar_x, bar_y, fill_width, bar_height))

            # 10) Dibujar borde exterior de la barra (negro)
            pygame.draw.rect(screen, (0, 0, 0), (bar_x, bar_y, bar_width, bar_height), 1)

            # 11) Opcional: segmentar la barra cada 20 puntos de vida
            num_segments = health.max_hp // 20
            if num_segments > 0:
                segment_width = bar_width / num_segments
                for i in range(1, num_segments):
                    x = bar_x + int(segment_width * i)
                    pygame.draw.line(
                        screen,
                        (0, 0, 0),
                        (x, bar_y),
                        (x, bar_y + bar_height)
                    )

        # El sistema NamePlateSystem se encarga del texto de nombres y títulos.
