import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite

class FireballRenderSystem:
    """
    Dibuja las fireballs creadas por el ECS como círculos.
    """
    def __init__(self, perf_log, radius=5, color=(255, 100, 0)):
        self.radius = radius
        self.color = color
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.FireballRenderSystem.update")
    def update(self, world, screen, camera):
        # Renderizar fireballs: sprite escalado o fallback círculo
        scale_map = world.components.get('Scale', {})
        sprite_map = world.components.get('Sprite', {})
        for eid, comp in world.components.get('FireballComponent', {}).items():
            pos = world.components['Position'][eid]
            x, y = camera.apply((pos.x, pos.y))
            if eid in sprite_map:
                sprite = sprite_map[eid]
                entity_scale = scale_map.get(eid, Scale()).scale
                scale_factor = entity_scale * camera.zoom
                image = pygame.transform.rotozoom(sprite.image, 0, scale_factor)
                rect = image.get_rect(center=(int(x), int(y)))
                screen.blit(image, rect.topleft)
            else:
                # fallback: círculo fijo
                pygame.draw.circle(screen, self.color, (int(x), int(y)), int(self.radius * camera.zoom))
