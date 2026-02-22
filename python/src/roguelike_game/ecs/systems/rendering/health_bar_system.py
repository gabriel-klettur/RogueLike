import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.core.identity import Identity, Faction

# Entities (Identity.name, lowercase) excluded from health bar rendering
EXCLUDED_IN_HEALTH_BAR = {
    'barbol',
}

class HealthBarSystem:
    """
    Sistema para renderizar barras de salud sobre entidades vivas.

    • Se dibuja solo para entidades con componentes Position y Health.
    • Se omiten las entidades con un DeathTimer activo (muertas).
    • La barra se centra sobre el sprite, ajustándose a la escala.
    • Se segmenta cada 20 puntos de vida para referencia visual.
    """

    def __init__(self, perf_log):
        """
        Inicializa el sistema de barras de salud.
        No mantiene estado interno.
        """
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
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

            # 2.2) Omitir ciertas clases por diseño usando lista de exclusión
            identity = world.components.get('Identity', {}).get(eid)
            name_lower = ''
            try:
                name_lower = str(getattr(identity, 'name', '')).lower()
            except Exception:
                name_lower = ''
            if name_lower in EXCLUDED_IN_HEALTH_BAR:
                continue
            # 2.3) Ocultar barra de vida sobre la cabeza de NPCs hostiles (facción EVIL)
            try:
                if identity and getattr(identity, 'faction', None) == Faction.EVIL:
                    # No dibujar barra sobre la cabeza; se mostrará solo en el HUD centrado
                    continue
            except Exception:
                pass

            # 3) Obtener componentes necesarios
            pos: Position = world.components['Position'][eid]
            health: Health = world.components['Health'][eid]
            sprite = world.components['Sprite'].get(eid)
            if sprite is None:
                continue
            scale_comp: Scale = world.components['Scale'].get(eid)

            # 4) Calcular ancho de la barra basado en el ancho del sprite y su escala
            orig_w, orig_h = sprite.image.get_size()
            entity_scale = scale_comp.scale if scale_comp else 1.0
            scaled_w = int(orig_w * entity_scale)
            bar_width = scaled_w
            bar_height = 5
            margin = 2

            # centrar barra en la parte superior del sprite escalado
            screen_x, screen_y = camera.apply((pos.x, pos.y))
            bar_x = screen_x + scaled_w / 2 - bar_width / 2
            bar_y = screen_y - margin - bar_height

            # 7) Calcular proporción de vida restante y ancho de relleno
            ratio = max(0, health.current_hp) / health.max_hp
            fill_width = int(bar_width * ratio)

            # 8) Colores (tinte amarillo en godmode)
            is_player = eid == getattr(world, 'player_entity', None)
            godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player
            bg_color = (50, 50, 50)
            fill_color = (255, 230, 100) if godmode else (0, 255, 0)
            border_color = (0, 0, 0)

            # 9) Dibujar la barra
            pygame.draw.rect(screen, bg_color, (bar_x, bar_y, bar_width, bar_height))
            pygame.draw.rect(screen, fill_color, (bar_x, bar_y, fill_width, bar_height))
            pygame.draw.rect(screen, border_color, (bar_x, bar_y, bar_width, bar_height), 1)

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