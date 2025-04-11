import time

class PlayerStats:
    def __init__(self, character_name):
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

        self.restore_cooldown = 5
        self.last_restore_time = -999

        self.shield_points = 0
        self.shield_activated_at = -999
        self.shield_cooldown = 20  # Cooldown entre usos
        self.shield_duration = 10  # Tiempo máximo activo (segundos)

    def take_damage(self):
        dmg_health = 10
        dmg_mana = 5
        dmg_energy = 15

        if self.shield_points > 0:
            absorbed = min(dmg_health, self.shield_points)
            self.shield_points -= absorbed
            dmg_health -= absorbed
            print(f"🛡️ Escudo absorbió {absorbed}. Restantes: {self.shield_points}")
            if self.shield_points <= 0:
                print("💥 Escudo destruido")

        self.health = max(0, self.health - dmg_health)
        self.mana = max(0, self.mana - dmg_mana)
        self.energy = max(0, self.energy - dmg_energy)

    def restore_all(self, state=None):
        now = time.time()
        if now - self.last_restore_time >= self.restore_cooldown:
            self.health = self.max_health
            self.mana = self.max_mana
            self.energy = self.max_energy
            self.last_restore_time = now

            if state:
                state.combat.effects.spawn_healing_aura()

            return True
        return False

    def activate_shield(self, amount=50):
        now = time.time()
        if now - self.shield_activated_at < self.shield_cooldown:
            print("⛔ Escudo aún en cooldown.")
            return False

        self.shield_points = amount
        self.shield_activated_at = now
        print(f"🛡️ Escudo activado con {amount} puntos.")
        return True

    def is_shield_active(self):
        if self.shield_points <= 0:
            return False
        return time.time() - self.shield_activated_at <= self.shield_duration
