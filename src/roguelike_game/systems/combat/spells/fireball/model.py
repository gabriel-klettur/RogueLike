# Path: src/roguelike_game/systems/combat/spells/fireball/model.py
import math
import pygame
from src.roguelike_engine.utils.loader import load_image

class FireballModel:
    """
    Estado puro de un Fireball: posición, trayectoria, vida,
    daño y callback de explosión.
    """
    def __init__(
        self,
        x: float,
        y: float,
        angle: float,
        speed: float = 15,
        lifespan: int = 60,
        damage: float = 10
    ):
        sprite = load_image("assets/projectiles/fireball.png", (64, 64))
        self.sprite = sprite
        # --- Mascara para colisiones ---
        self.mask   = pygame.mask.from_surface(sprite)
        # ---------------------------------
        self.size   = (64, 64)
        self.x      = x
        self.y      = y
        radians     = math.radians(angle)
        self.angle  = radians
        self.speed  = speed
        self.lifespan = lifespan
        self.age    = 0
        self.dx     = math.cos(radians) * speed
        self.dy     = math.sin(radians) * speed
        self.damage = damage
        # Explosion se instanciará en tiempo de explosión para usar coords actuales
        self.explosion = None
        self.on_explode = lambda ex, ey: None
        self.alive   = True

    def is_finished(self) -> bool:
        return not self.alive