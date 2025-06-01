# Path: src/roguelike_game/systems/combat/spells/lightning/model.py
import math
import random

class LightningModel:
    """
    Modelo puro de un rayo de lightning:
    - start: punto de origen
    - end: punto de destino
    - points: lista de vértices del zig-zag
    - lifetime: ciclos restantes
    """
    def __init__(self, start_pos: tuple[float,float], end_pos: tuple[float,float],
                 segments: int = 10, offset: int = 15, lifetime: int = 8):
        self.start = start_pos
        self.end   = end_pos
        self.segments = segments
        self.offset   = offset
        self.lifetime = lifetime
        self.max_lifetime = lifetime
        self._generate_points()

    def _generate_points(self):
        sx, sy = self.start
        ex, ey = self.end
        self.points = [(sx, sy)]
        for i in range(1, self.segments):
            t = i / self.segments
            x = sx + (ex - sx) * t + random.randint(-self.offset, self.offset)
            y = sy + (ey - sy) * t + random.randint(-self.offset, self.offset)
            self.points.append((x, y))
        self.points.append((ex, ey))

    def update(self):
        # Decrece vida
        self.lifetime -= 1
    

    def is_finished(self) -> bool:
        return self.lifetime <= 0