# Path: src/roguelike_game/systems/combat/spells/sphere_magic_shield/model.py
import time

class SphereMagicShieldModel:
    """
    Estado del escudo mágico: posición ligada al jugador,
    radio del círculo, duración y semilla para animación.
    """
    def __init__(self, player, radius=80, duration=5.0):
        self.player = player
        self.base_radius = radius
        self.radius = radius
        self.duration = duration
        self.start_time = time.time()
        # color pulsante
        self.color = (150, 200, 255)
    
    def is_finished(self) -> bool:
        return (time.time() - self.start_time) > self.duration

    def elapsed(self) -> float:
        return time.time() - self.start_time