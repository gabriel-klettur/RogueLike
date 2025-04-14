import pygame
from roguelike_project.utils.loader import load_image
from roguelike_project.config import DEBUG


class Building:
    def __init__(self, x, y, image_path, solid=True, scale=None):
        self.x = x
        self.y = y
        self.solid = solid
        self.image_path = image_path  # ✅ ← Agregá esta línea

        self.image = load_image(image_path)
        if scale:
            self.image = pygame.transform.scale(self.image, scale)

        self.rect = pygame.Rect(x, y, self.image.get_width(), self.image.get_height())

    def render(self, screen, camera):
        # 🖼️ Dibujar el sprite escalado
        scaled_image = pygame.transform.scale(
            self.image,
            camera.scale((self.image.get_width(), self.image.get_height()))
        )
        screen.blit(scaled_image, camera.apply((self.x, self.y)))

        # 🟨 Mostrar el área de colisión si es sólido y DEBUG está activo
        if self.solid and DEBUG:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 255), scaled_rect, 2)

    def update(self):
        pass  # En el futuro podrías animar, detectar colisiones, etc.
