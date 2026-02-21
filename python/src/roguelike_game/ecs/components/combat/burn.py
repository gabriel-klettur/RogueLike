from dataclasses import dataclass
import time


@dataclass
class BurnComponent:
    damage_per_tick: int
    duration: float
    tick_period: float
    start_time: float
    last_tick_time: float
    applier: int | None = None
