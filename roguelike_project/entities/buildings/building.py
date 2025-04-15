import pygame
from roguelike_project.utils.loader import load_image
import roguelike_project.config as config

class Building:
    def __init__(self, x, y, image_path, solid=True, scale=None):
        self.x = x
        self.y = y
        self.solid = solid
        self.image_path = image_path
        self.scaled_cache = {}  # zoom: surface escalada

        self.image = load_image(image_path)

        # Escalado inicial y guardado del tamaño original
        if scale:
            self.image = pygame.transform.scale(self.image, scale)
            self.original_scale = scale
        else:
            self.original_scale = self.image.get_size()

        self.rect = pygame.Rect(x, y, self.image.get_width(), self.image.get_height())

    def render(self, screen, camera):
        zoom = round(camera.zoom, 2)

        if zoom not in self.scaled_cache:
            self.scaled_cache[zoom] = pygame.transform.scale(
                self.image,
                camera.scale(self.image.get_size())
            )

        scaled_image = self.scaled_cache[zoom]
        screen.blit(scaled_image, camera.apply((self.x, self.y)))

        if self.solid and config.DEBUG:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 255), scaled_rect, 2)

    def update(self):
        pass

    def resize(self, new_width, new_height):
        self.image = pygame.transform.scale(load_image(self.image_path), (new_width, new_height))
        self.rect = pygame.Rect(self.x, self.y, new_width, new_height)
        self.scaled_cache.clear()

    def reset_to_original_size(self):
        if self.original_scale:
            self.resize(*self.original_scale)
            print(f"↩️ Tamaño reseteado a original: {self.original_scale}")
        else:
            print("⚠️ No se encontró escala original para este edificio.")
