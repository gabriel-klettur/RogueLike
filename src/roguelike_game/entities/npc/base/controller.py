
# Path: src/roguelike_game/entities/npc/base/controller.py
from .interfaces import IController

class BaseNPCController(IController):
    """
    Controlador base para NPCs.  
    Subclases deberían sobreescribir `update()`.
    """
    def __init__(self, model):
        self.model = model

    def update(self, state):
        """
        Lógica genérica: si el modelo está muerto, no hace nada.
        """
        if not getattr(self.model, "alive", True):
            return
        # En la siguiente iteración se añadirá IA común