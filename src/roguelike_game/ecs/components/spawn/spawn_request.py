from dataclasses import dataclass
from typing import Optional, Tuple

@dataclass
class SpawnRequest:
    """Componente que solicita creación de un NPC en una posición dada."""
    prototype: str
    position: Tuple[int, int]
    # Metadata opcional para rastrear oleadas
    spawner_eid: Optional[int] = None
    wave_idx: Optional[int] = None