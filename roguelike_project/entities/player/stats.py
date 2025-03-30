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

    def take_damage(self):
        self.health = max(0, self.health - 10)
        self.mana = max(0, self.mana - 5)
        self.energy = max(0, self.energy - 15)
        print("💥 Daño recibido: -10 vida, -5 maná, -15 energía")

    def restore_all(self):
        now = time.time()
        if now - self.last_restore_time >= self.restore_cooldown:
            self.health = self.max_health
            self.mana = self.max_mana
            self.energy = self.max_energy
            self.last_restore_time = now
            print("🧪 Barras restauradas al máximo")
        else:
            print("⌛ Aún en cooldown...")
