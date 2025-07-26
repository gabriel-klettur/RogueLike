from dataclasses import dataclass

@dataclass
class MagicSpellBarComponent:
    """
    Componente que contiene información para dibujar la barra de progreso del hechizo actual.
    duration: tiempo total de la fase (prepare, channel, cooldown)
    start_time: marca de tiempo al inicio de la fase
    active: si la barra debe mostrarse
    state: nombre de la fase ('prepare', 'channel', 'cooldown')
    """
    duration: float = 0.0
    start_time: float = 0.0
    active: bool = False
    state: str = ""
