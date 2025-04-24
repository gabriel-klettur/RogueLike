# roguelike_project/systems/combat/spells/teleport/controller.py
import time
from roguelike_project.systems.combat.spells.teleport.model import TeleportModel

class TeleportController:
    def __init__(self, model: TeleportModel):
        self.m = model

    def update(self):
        # ¿Cambiamos a fase “in” a mitad de camino?
        if self.m.should_switch_phase():
            self.m.phase = "in"
            # reiniciamos el reloj para fase “in”
            self.m.start_time = time.time()

    def is_finished(self) -> bool:
        """Le dice al SpellsSystem cuándo descartar este controller."""
        return self.m.is_finished()