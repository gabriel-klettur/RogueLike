class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world, screen, camera):
        # Iterate entities with Position and Sprite and render anchored to map
        for eid in world.get_entities_with('Position', 'Sprite'):
            pos = world.components['Position'][eid]
            sprite = world.components['Sprite'][eid]
            # Convert world position to screen position
            screen_pos = camera.apply((pos.x, pos.y))
            screen.blit(sprite.image, screen_pos)