import pygame

class Sprite:
    def __init__(self, image):
        # Accept either a Surface or a file path
        if isinstance(image, pygame.Surface):
            self.image = image
        else:
            # Load from path and convert alpha for transparency
            self.image = pygame.image.load(image).convert_alpha()