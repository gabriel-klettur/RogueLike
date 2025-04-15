import pygame
from roguelike_project.utils.loader import load_image
from roguelike_project.config import DEBUG

class Building:
    def __init__(self, x, y, image_path, solid=True, scale=None):
        self.x = x
        self.y = y
        self.solid = solid
        self.image_path = image_path
        self.scaled_cache = {}  # zoom: surface escalada

        self.image = load_image(image_path)
        if scale:
            self.image = pygame.transform.scale(self.image, scale)

        self.rect = pygame.Rect(x, y, self.image.get_width(), self.image.get_height())

    def render(self, screen, camera):
            zoom = round(camera.zoom, 2)

            # 📦 Verificar caché de escalado
            if zoom not in self.scaled_cache:
                self.scaled_cache[zoom] = pygame.transform.scale(
                    self.image,
                    camera.scale(self.image.get_size())
                )

            scaled_image = self.scaled_cache[zoom]
            screen.blit(scaled_image, camera.apply((self.x, self.y)))

            if self.solid and DEBUG:
                scaled_rect = pygame.Rect(
                    camera.apply(self.rect.topleft),
                    camera.scale(self.rect.size)
                )
                pygame.draw.rect(screen, (255, 255, 255), scaled_rect, 2)

    def update(self):
        pass  # En el futuro podrías animar, detectar colisiones, etc.

    def resize(self, new_width, new_height):
        self.image = pygame.transform.scale(load_image(self.image_path), (new_width, new_height))
        self.rect = pygame.Rect(self.x, self.y, new_width, new_height)
        self.scaled_cache.clear()  # 🔁 Muy importante