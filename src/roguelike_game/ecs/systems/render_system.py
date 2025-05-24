class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world):
        # Iterate entities with Position and Sprite
        for eid in world.get_entities_with('Position', 'Sprite'):
            pos = world.components['Position'][eid]
            sprite = world.components['Sprite'][eid]
            self.screen.blit(sprite.image, (pos.x, pos.y))