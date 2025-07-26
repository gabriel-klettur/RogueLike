from dataclasses import dataclass, field
import time

@dataclass
class GrayscaleComponent:
    """
    Componente para indicar que la vista debe mostrarse en escala de grises.
    """
    start_time: float = field(default_factory=time.time)