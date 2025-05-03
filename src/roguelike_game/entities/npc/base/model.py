#Path: src/roguelike_game/entities/npc/base/model.py

from .interfaces import IModel

class BaseNPCModel(IModel):
    """
    Modelo genérico de NPC: posición, nombre y estado de vida.
    Las subclases deben definir salud, velocidad y lógica en take_damage().
    """
    def __init__(self, x: float, y: float, name: str):
        self.x = x
        self.y = y
        self.name = name
        self.alive = True

    def take_damage(self, amount: float):
        """
        Comportamiento por defecto: marcar muerto si health llega ≤ 0.
        Subclases pueden llamar super().take_damage(...)
        """
        # Si la subclase define `self.health`, actualizarlo:
        if hasattr(self, "health"):
            self.health -= amount
            if self.health <= 0:
                self.alive = False
        else:
            # Si no hay health, asumimos vida binaria
            self.alive = False
