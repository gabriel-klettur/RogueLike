# Path: src/roguelike_game/ecs/components/rendering/sprite.py
import pygame
from roguelike_engine.utils.loader import load_image

class Sprite:
    def __init__(self, image):
        # Accept either a Surface or a file path
        if isinstance(image, pygame.Surface):
            self.image = image
        else:
            # Load from path using cached loader
            self.image = load_image(image)