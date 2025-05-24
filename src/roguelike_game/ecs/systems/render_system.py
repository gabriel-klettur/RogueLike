import pygame
from ..components.scale import Scale

class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world, screen, camera):
        # Iterate entities with Position and Sprite and render anchored to map
        for eid in world.get_entities_with('Position', 'Sprite'):
            pos = world.components['Position'][eid]
            sprite = world.components['Sprite'][eid]
            # Apply scaling if present
            scale_comp: Scale = world.components['Scale'].get(eid)
            image = sprite.image
            if scale_comp and scale_comp.scale != 1.0:
                w, h = image.get_size()
                image = pygame.transform.scale(
                    image, (int(w * scale_comp.scale), int(h * scale_comp.scale))
                )
            # Convert world position to screen position
            screen_pos = camera.apply((pos.x, pos.y))
            screen.blit(image, screen_pos)