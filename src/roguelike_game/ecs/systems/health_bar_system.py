import pygame
from ..components.position import Position
from ..components.health import Health
from ..components.scale import Scale
from ..components.sprite import Sprite

class HealthBarSystem:
    """
    Sistema para renderizar barras de salud centradas sobre las entidades con Health.
    """
    def __init__(self):
        pass

    def update(self, world, screen, camera):
        for eid in world.get_entities_with('Position', 'Health'):
            pos: Position = world.components['Position'][eid]
            health: Health = world.components['Health'][eid]
            # Posición en pantalla
            screen_x, screen_y = camera.apply((pos.x, pos.y))
            # Obtener ancho de sprite escalado
            sprite: Sprite = world.components['Sprite'][eid]
            image = sprite.image
            scale_comp: Scale = world.components['Scale'].get(eid)
            width = image.get_width()
            if scale_comp and scale_comp.scale != 1.0:
                width = int(width * scale_comp.scale)
            # Dimensiones de la barra
            bar_width = width
            bar_height = 5
            margin = 2
            # Posicionar arriba del sprite
            bar_x = screen_x
            bar_y = screen_y - margin - bar_height
            # Calcular proporción y relleno
            ratio = max(0, health.current_hp) / health.max_hp
            fill_width = int(bar_width * ratio)
            # Dibujar fondo (gris)
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, bar_width, bar_height))
            # Dibujar relleno (verde)
            pygame.draw.rect(screen, (0, 255, 0), (bar_x, bar_y, fill_width, bar_height))
