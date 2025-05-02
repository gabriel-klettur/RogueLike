# Path: src/roguelike_game/entities/player/model/stats_model.py
import time

class PlayerStats:
    """
    Modelo de estadísticas: salud, mana, energía y cooldowns.
    """
    def __init__(self, character_name: str):
        if character_name == "valkyria":
            self.max_health = 120
            self.max_mana = 80
            self.max_energy = 60
        else:
            self.max_health = 100
            self.max_mana = 50
            self.max_energy = 100
        self.health = self.max_health
        self.mana = self.max_mana
        self.energy = self.max_energy

        self.restore_cooldown = 5.0
        self.last_restore_time = -float('inf')

        self.shield_cooldown = 20.0
        self.shield_duration = 10.0
        self.last_shield_time = -float('inf')
        self.shield_points = 0

        self.firework_cooldown = 5.0
        self.last_firework_time = -float('inf')

        self.smoke_cooldown = 6.0
        self.last_smoke_time = -float('inf')

        self.lightning_cooldown = 4.0
        self.last_lightning_time = -float('inf')

        self.pixel_fire_cooldown = 3.0
        self.last_pixel_fire_time = -float('inf')

    def take_damage(self):
        dmg_health = 10
        dmg_mana = 5
        dmg_energy = 15
        if self.shield_points > 0:
            absorbed = min(dmg_health, self.shield_points)
            self.shield_points -= absorbed
            dmg_health -= absorbed
        self.health = max(0, self.health - dmg_health)
        self.mana   = max(0, self.mana   - dmg_mana)
        self.energy = max(0, self.energy - dmg_energy)

    def restore_all(self):
        now = time.time()
        if now - self.last_restore_time < self.restore_cooldown:
            return False
        self.health = self.max_health
        self.mana   = self.max_mana
        self.energy = self.max_energy
        self.last_restore_time = now
        return True

    def activate_shield(self, points: int = 50):
        now = time.time()
        if now - self.last_shield_time < self.shield_cooldown:
            return False
        self.shield_points      = points
        self.last_shield_time   = now
        return True