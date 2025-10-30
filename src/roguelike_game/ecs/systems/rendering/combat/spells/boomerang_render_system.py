import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite


class BoomerangRenderSystem:
    def __init__(self, perf_log=None, radius=6, color=(200, 200, 50)):
        self.radius = radius
        self.color = color
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'BoomerangRenderSystem.update')
    def update(self, world, screen, camera):
        scale_map = world.components.get('Scale', {})
        sprite_map = world.components.get('Sprite', {})
        for eid, comp in world.components.get('BoomerangComponent', {}).items():
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
                pygame.draw.circle(screen, self.color, (int(x), int(y)), int(self.radius * camera.zoom))
