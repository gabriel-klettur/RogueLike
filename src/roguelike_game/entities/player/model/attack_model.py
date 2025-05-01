import time

class PlayerAttack:
    """
    Modelo de ataque básico: trackea cooldown y tiempo de último ataque.
    """
    def __init__(self, player):
        self.player = player
        self.last_attack_time = 0
        self.attack_cooldown = 0.2  # segundos entre ataques

    def perform_basic_attack(self):
        now = time.time()
        if now - self.last_attack_time < self.attack_cooldown:
            return False
        self.last_attack_time = now
        # Aquí dispararías un evento o callback para la vista/effects
        return True