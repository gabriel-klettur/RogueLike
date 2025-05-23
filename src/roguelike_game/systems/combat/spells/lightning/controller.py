# Path: src/roguelike_game/systems/combat/spells/lightning/controller.py
import pygame
from pygame.math import Vector2
from roguelike_game.systems.combat.spells.lightning.model import LightningModel

class LightningController:
    """
    Controlador que actualiza el modelo de lightning,
    comprobando colisión con enemigos si se desea.
    """
    def __init__(self, model: LightningModel):
        self.model = model        

    def update(self):
        # Vida y colisiones opcionales
        self.model.update()
        # (Aquí podrías insertar lógica de daño continuo si quieres)
    
    def is_finished(self):
        return self.model.is_finished()