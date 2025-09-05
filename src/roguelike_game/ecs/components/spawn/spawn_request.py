from dataclasses import dataclass
from typing import Optional, Tuple

@dataclass
class SpawnRequest:
    """Componente que solicita creación de un NPC en una posición dada."""
    prototype: str
    position: Tuple[int, int]
    # Si se provee, la entidad creada usará este instance_id persistente
    instance_id: Optional[str] = None
    # Metadata opcional para rastrear oleadas
    spawner_eid: Optional[int] = None
    wave_idx: Optional[int] = None
    # Metadata opcional de defensa: si se especifica, el NPC defenderá un área circular
    # center_x/center_y en píxeles y radio en píxeles
    defend_center: Optional[Tuple[float, float]] = None
    defend_radius_px: Optional[float] = None
    # Si True, aplica leash al radio de defensa; si False, sin leash (persecución libre)
    defend_leash: Optional[bool] = None
    # Forma del área de defensa: "circle" (por defecto) o "square"
    defend_shape: Optional[str] = None