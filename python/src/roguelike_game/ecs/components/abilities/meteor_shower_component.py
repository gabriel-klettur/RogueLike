class MeteorShowerComponent:
    def __init__(self, *, count: int, interval: float, area_radius: float, impact_damage: float, impact_radius: float, owner: int, spell_key: str):
        self.count = int(count)
        self.interval = float(interval)
        self.area_radius = float(area_radius)
        self.impact_damage = float(impact_damage)
        self.impact_radius = float(impact_radius)
        self.owner = owner
        self.spell_key = spell_key
        self.start_time = 0.0
        self.last_spawn_time = 0.0
        self.spawns_done = 0
