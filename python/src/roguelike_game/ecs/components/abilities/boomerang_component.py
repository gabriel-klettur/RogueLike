class BoomerangComponent:
    def __init__(self, dir_x: float, dir_y: float, speed: float, damage: float, range: float, return_speed: float, passes_through: bool, caster: int, spawn_pos: tuple[float, float], hit_radius: float = 12.0, spell_key: str | None = None):
        self.dir_x = float(dir_x)
        self.dir_y = float(dir_y)
        self.speed = float(speed)
        self.damage = float(damage)
        self.range = float(range)
        self.return_speed = float(return_speed)
        self.passes_through = bool(passes_through)
        self.caster = caster
        self.spawn_pos = (float(spawn_pos[0]), float(spawn_pos[1])) if isinstance(spawn_pos, (list, tuple)) and len(spawn_pos) >= 2 else (0.0, 0.0)
        self.state = 'outbound'
        self.age = 0
        self.distance = 0.0
        self.hit_radius = float(hit_radius)
        self.hit_targets = set()
        self.spell_key = str(spell_key) if spell_key else ""
