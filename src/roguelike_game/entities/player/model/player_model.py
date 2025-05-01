import pygame
from .stats_model import PlayerStats
from .movement_model import PlayerMovement
from .attack_model import PlayerAttack

class PlayerModel:
    """
    Modelo puro de jugador: posición, estado y componentes.
    """
    def __init__(self, x: float, y: float, character_name: str = "first_hero"):
        self.x = x
        self.y = y
        self.character_name = character_name
        # La vista se encargará de cargar el sprite_size real
        self.sprite_size = (128, 128)
        self.direction = "down"
        self.is_walking = False

        self.rect = None
        self.hitbox_obj = None

        # Componentes MVC internos
        self.stats    = PlayerStats(character_name)
        self.movement = PlayerMovement(self)
        self.attack   = PlayerAttack(self)

    def center(self) -> tuple[float, float]:
        """Centro del sprite (para apuntado)."""
        return (self.x + self.sprite_size[0]/2,
                self.y + self.sprite_size[1]/2)

    def hitbox(self) -> pygame.Rect:
        """Rectángulo de colisión (hitbox)."""
        return pygame.Rect(
            self.x + 20,
            self.y + 96,
            56,
            28
        )