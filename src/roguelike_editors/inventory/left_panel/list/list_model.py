from dataclasses import dataclass
from typing import Optional


@dataclass
class ListModel:
    """
    Modelo para la lista del panel izquierdo: maneja la entidad seleccionada.
    """
    selected_eid: Optional[str] = None
