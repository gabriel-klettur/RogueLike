from dataclasses import dataclass, field
import time


@dataclass
class DyingTag:
    start_time: float = field(default_factory=time.time)
