from __future__ import annotations
import time
from typing import Optional


class ForceFieldComponent:
    """
    Campo de fuerza que aplica una aceleración (impulso a la velocidad) a entidades
    dentro de un radio. Modo "pull" atrae hacia el centro; "push" empuja hacia afuera.
    """
    def __init__(
        self,
        *,
        radius: float,
        force: float,
        mode: str = "pull",
        duration: float = 0.0,
        owner: Optional[int] = None,
        spell_key: str = "",
        start_time: Optional[float] = None,
        anchor_eid: Optional[int] = None,
        follow: bool = False,
        affect_owner: bool = False,
        affect_allies: bool = False,
        affect_neutrals: bool = False,
        affect_enemies: bool = True,
        drag: float = 0.0,
    ) -> None:
        self.radius = float(max(0.0, radius))
        self.force = float(max(0.0, force))
        self.mode = str(mode or "pull").lower()
        self.duration = float(max(0.0, duration))
        self.owner = owner
        self.spell_key = spell_key
        self.start_time = float(start_time) if start_time is not None else time.time()
        # Si follow es True y anchor_eid está definido, el campo seguirá al ancla (e.g., caster)
        self.anchor_eid = anchor_eid
        self.follow = bool(follow)
        # Filtros de afectación por relación
        self.affect_owner = bool(affect_owner)
        self.affect_allies = bool(affect_allies)
        self.affect_neutrals = bool(affect_neutrals)
        self.affect_enemies = bool(affect_enemies)
        # Amortiguación opcional (0..1)
        try:
            self.drag = float(drag)
        except Exception:
            self.drag = 0.0
        if self.drag < 0.0:
            self.drag = 0.0
        if self.drag > 0.98:
            self.drag = 0.98

    def is_expired(self, now: Optional[float] = None) -> bool:
        if self.duration <= 0:
            return False
        t = time.time() if now is None else float(now)
        return t >= self.start_time + self.duration
