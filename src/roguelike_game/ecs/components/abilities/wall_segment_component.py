import time
from typing import Optional


class WallSegmentComponent:
    """
    Segmento de muro, soporta orientación (OBB) con ángulo en grados.
    - width, height: dimensiones locales (ancho/largo) del rectángulo
    - hp: vida del segmento (si llega a 0, desaparece)
    - duration: tiempo de vida (segundos); 0 o <0 => infinito
    - blocks_projectiles / blocks_units: flags de colisión lógica
    - owner: eid del caster
    - spell_key: id del spell
    - orient: etiqueta informativa (no funcional), p.ej. 'horizontal'|'vertical'
    - angle_deg: ángulo del OBB en grados (0 = eje X positivo); perpendicularidad depende del resolver
    """
    def __init__(
        self,
        *,
        width: float,
        height: float,
        hp: float,
        duration: float,
        blocks_projectiles: bool = True,
        blocks_units: bool = True,
        owner: Optional[int] = None,
        spell_key: str = "",
        orient: str = "horizontal",
        angle_deg: float = 0.0,
    ) -> None:
        self.width = float(width)
        self.height = float(height)
        self.hp = float(hp)
        self.duration = float(duration)
        self.blocks_projectiles = bool(blocks_projectiles)
        self.blocks_units = bool(blocks_units)
        self.owner = owner
        self.spell_key = str(spell_key)
        self.orient = orient if orient in ("horizontal", "vertical") else "horizontal"
        # Ángulo y caches para colisión/render
        self.angle_deg = float(angle_deg)
        try:
            import math
            a = math.radians(self.angle_deg)
            self.cos_a = math.cos(a)
            self.sin_a = math.sin(a)
        except Exception:
            self.cos_a = 1.0
            self.sin_a = 0.0
        self.half_w = self.width * 0.5
        self.half_h = self.height * 0.5
        self.start_time = time.time()
