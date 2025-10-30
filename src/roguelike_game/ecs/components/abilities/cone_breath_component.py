from __future__ import annotations
import time
from typing import Optional


class ConeBreathComponent:
    """
    Mantiene el estado de un aliento en cono anclado al caster.
    El sistema spawnea una Hitbox efímera cada tick para aplicar daño en arco.
    """

    def __init__(
        self,
        *,
        owner: int,
        arc_degrees: float,
        length: float,
        damage_per_tick: float,
        tick_period: float,
        duration: float,
        spell_key: str = "",
        preset_id: Optional[str] = None,
        preset_scale: float = 1.0,
        follow_owner: bool = True,
        rotate_with_owner: bool = True,
        offset: float = 0.0,
        initial_direction: Optional[tuple[float, float]] = None,
    ) -> None:
        self.owner = int(owner)
        self.arc_degrees = float(arc_degrees)
        self.length = float(length)
        self.damage_per_tick = float(damage_per_tick)
        self.tick_period = float(tick_period)
        self.duration = float(duration)
        self.spell_key = str(spell_key)
        self.preset_id = preset_id if isinstance(preset_id, str) else None
        try:
            self.preset_scale = float(preset_scale)
        except Exception:
            self.preset_scale = 1.0
        self.follow_owner = bool(follow_owner)
        self.rotate_with_owner = bool(rotate_with_owner)
        self.offset = float(offset)
        # Dirección fija opcional (para NPCs). Si None y rotate_with_owner=True, se usará mouse.
        self.initial_direction = tuple(initial_direction) if isinstance(initial_direction, (list, tuple)) and len(initial_direction) >= 2 else None
        # Timestamps
        self.start_time = time.time()
        self.last_tick_time = 0.0  # primer tick inmediato
        # Posición actual (actualizada por el sistema)
        self.current_pos = (0.0, 0.0)

    def is_finished(self) -> bool:
        return (time.time() >= (self.start_time + self.duration)) if self.duration > 0 else False
