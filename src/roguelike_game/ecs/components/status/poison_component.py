import time
from dataclasses import dataclass


@dataclass
class PoisonComponent:
    """Status component representing a poison effect (green flash).

    Fields mirror burn to ease future reuse: a DoT system can optionally
    consume damage_per_tick and tick_period, while the FlashSystem only
    needs start_time and (optionally) tick_period for blink cadence.
    """
    damage_per_tick: int
    duration: float
    tick_period: float
    start_time: float
    last_tick_time: float
    applier: int | None = None

    @staticmethod
    def create(duration: float, damage_per_tick: int = 1, tick_period: float = 1.0, applier: int | None = None) -> "PoisonComponent":
        now = time.time()
        return PoisonComponent(
            damage_per_tick=int(damage_per_tick),
            duration=float(duration),
            tick_period=float(tick_period),
            start_time=now,
            last_tick_time=now,
            applier=applier,
        )
