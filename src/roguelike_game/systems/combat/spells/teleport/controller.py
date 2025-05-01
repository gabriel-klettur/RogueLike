# Path: src/roguelike_game/systems/combat/spells/teleport/controller.py
import time
from src.roguelike_game.systems.combat.spells.teleport.model import TeleportModel

class TeleportController:
    def __init__(self, model: TeleportModel):
        self.model = model

    def update(self):
        # ¿Cambiamos a fase “in” a mitad de camino?
        if self.model.should_switch_phase():
            self.model.phase = "in"
            # reiniciamos el reloj para fase “in”
            self.model.start_time = time.time()

    def is_finished(self) -> bool:
        """Le dice al SpellsSystem cuándo descartar este controller."""
        return self.model.is_finished()