import pygame

class Sprite:
    def __init__(self, image_path: str):
        # Load the image and convert alpha for transparency
        self.image = pygame.image.load(image_path).convert_alpha()