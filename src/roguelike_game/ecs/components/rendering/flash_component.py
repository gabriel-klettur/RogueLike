from dataclasses import dataclass
import time

@dataclass
class FlashComponent:
    """
    Componente para efecto flash de color en sprites.
    """
    color: tuple         # Color RGB para el flash, e.g. (255,255,255)
    duration: float      # Duración en segundos
    start_time: float = time.time()  # Timestamp de inicio
