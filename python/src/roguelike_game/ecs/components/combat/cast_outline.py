from dataclasses import dataclass
from typing import Tuple
import time


@dataclass
class CastOutline:
    """Outline temporal para canalizado de hechizos.

    Atributos:
        start_time: Inicio del canalizado (segundos epoch).
        duration: Duración total del canalizado en segundos.
        color_from: Color RGB inicial.
        color_to: Color RGB final.
        width: Grosor de la línea.
    """
    start_time: float
    duration: float
    color_from: Tuple[int, int, int] = (0, 128, 255)
    color_to: Tuple[int, int, int] = (0, 255, 0)
    width: int = 3

    @staticmethod
    def create(duration: float, color_from=(0, 128, 255), color_to=(0, 255, 0), start_time: float | None = None, width: int = 3) -> "CastOutline":
        st = time.time() if start_time is None else float(start_time)
        return CastOutline(start_time=st, duration=float(duration), color_from=tuple(color_from), color_to=tuple(color_to), width=int(width))
