# Path: src/roguelike_game/entities/player/model/stats_model.py
import time

from src.roguelike_game.entities.player.config_player import (
    PLAYER_STATS,
    PLAYER_SKILLS
)

class PlayerStats:
    """
    Modelo de estadísticas: salud, mana, energía y cooldowns.
    """
    def __init__(self, character_name: str):
        if character_name == "valkyria":
            self.max_health = PLAYER_STATS["valkyria"]["max_health"]
            self.max_mana = PLAYER_STATS["valkyria"]["max_mana"]
            self.max_energy = PLAYER_STATS["valkyria"]["max_energy"]
        else:
            self.max_health = PLAYER_STATS["first_hero"]["max_health"]
            self.max_mana = PLAYER_STATS["first_hero"]["max_mana"]
            self.max_energy = PLAYER_STATS["first_hero"]["max_energy"]
        
        self.health = self.max_health
        self.mana = self.max_mana
        self.energy = self.max_energy

        self.restore_cooldown = PLAYER_SKILLS[character_name]["restore"]["cooldown"]
        self.last_restore_time = -float('inf')

        self.shield_cooldown = PLAYER_SKILLS[character_name]["shield"]["cooldown"]
        self.shield_duration = PLAYER_SKILLS[character_name]["shield"]["duration"]
        self.last_shield_time = -float('inf')
        self.shield_points = PLAYER_SKILLS[character_name]["shield"]["points"]

        self.firework_cooldown = PLAYER_SKILLS[character_name]["firework"]["cooldown"]
        self.last_firework_time = -float('inf')

        self.smoke_cooldown = PLAYER_SKILLS[character_name]["smoke"]["cooldown"]
        self.last_smoke_time = -float('inf')

        self.lightning_cooldown = PLAYER_SKILLS[character_name]["lightning"]["cooldown"]
        self.last_lightning_time = -float('inf')

        self.pixel_fire_cooldown = PLAYER_SKILLS[character_name]["pixel_fire"]["cooldown"]
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