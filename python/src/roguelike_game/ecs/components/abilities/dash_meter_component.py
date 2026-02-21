import time
from dataclasses import dataclass

@dataclass
class DashMeterComponent:
    """
    Recurso de cargas de dash por entidad.

    - total: número total de cargas disponibles.
    - current: cargas actuales disponibles.
    - recharge_s: segundos para recargar 1 carga.
    - policy: 'sequential' (por defecto; recarga una carga a la vez).
    - progress: progreso de la recarga [0..1] de la siguiente carga.
    - last_time: timestamp del último avance de recarga.
    """
    total: int
    current: int
    recharge_s: float
    policy: str = 'sequential'
    progress: float = 0.0
    last_time: float = 0.0

    def ensure_timer(self):
        if self.last_time == 0.0:
            self.last_time = time.time()
