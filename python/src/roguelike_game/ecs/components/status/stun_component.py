import time
from dataclasses import dataclass


@dataclass
class StunComponent:
    """Status component that prevents an entity from acting/moving for a duration."""
    duration: float
    start_time: float

    @staticmethod
    def create(duration: float) -> "StunComponent":
        now = time.time()
        return StunComponent(duration=float(duration), start_time=now)
