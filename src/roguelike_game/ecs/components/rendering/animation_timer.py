from dataclasses import dataclass

@dataclass
class AnimationTimer:
    """
    Componente que controla el intervalo de actualización de frames.
    last_time: marca de tiempo del último cambio de frame.
    interval: segundos a esperar entre frames.
    """
    last_time: float
    interval: float
