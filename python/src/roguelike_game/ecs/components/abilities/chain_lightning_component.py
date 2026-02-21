from __future__ import annotations
import time
from typing import Set, Tuple, Optional


class ChainLightningComponent:
    """
    Componente ECS para Chain Lightning (rayo que rebota entre objetivos).
    Mantiene estado de chaining para que el sistema pueda resolver saltos y aplicar daño.
    """
    def __init__(
        self,
        *,
        start_pos: Tuple[float, float],
        damage: float,
        max_bounces: int,
        range: float,
        damage_decay: float = 1.0,
        owner: Optional[int] = None,
        spell_key: str = "",
    ) -> None:
        self.start_time = time.time()
        self.current_pos = (float(start_pos[0]), float(start_pos[1]))
        self.damage = float(damage)
        self.max_bounces = int(max(0, max_bounces))
        self.range = float(max(0.0, range))
        self.damage_decay = float(damage_decay) if damage_decay is not None else 1.0
        # Estado de chaining
        self.bounces_left = int(self.max_bounces)
        self.already_hit: Set[int] = set()
        self.owner = owner
        self.spell_key = spell_key

    def is_finished(self) -> bool:
        return self.bounces_left <= 0
