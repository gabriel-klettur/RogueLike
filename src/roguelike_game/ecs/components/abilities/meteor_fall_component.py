class MeteorFallComponent:
    def __init__(self, *, target_x: float, target_y: float, height_px: float, fall_speed_px_s: float,
                 impact_damage: float, impact_radius: float, owner: int, spell_key: str):
        self.target_x = float(target_x)
        self.target_y = float(target_y)
        self.height_px = float(height_px)
        self.fall_speed_px_s = float(fall_speed_px_s)
        self.impact_damage = float(impact_damage)
        self.impact_radius = float(impact_radius)
        self.owner = owner
        self.spell_key = spell_key
        self._last_time = 0.0
