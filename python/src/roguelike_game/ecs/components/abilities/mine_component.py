import time
from typing import Any, Dict, Optional


class MineComponent:
    """
    Componente ECS para una "mina": espera un tiempo de armado y detona
    cuando una entidad objetivo entra en su radio de activación.
    
    - trigger_radius: radio de activación para detonar
    - arming_time: segundos antes de estar armada
    - ttl: tiempo de vida máximo (segundos) antes de autodestruirse sin detonar
    - payload: definición del efecto al detonar (e.g., {"explosion": {"radius": 140, "damage": 28}})
    - owner: eid del caster que plantó la mina
    - spell_key: id del spell en spells.json
    """
    def __init__(
        self,
        *,
        trigger_radius: float,
        arming_time: float,
        ttl: float,
        payload: Optional[Dict[str, Any]] = None,
        owner: Optional[int] = None,
        spell_key: str = "",
    ) -> None:
        now = time.time()
        self.trigger_radius = float(trigger_radius)
        self.arming_time = max(0.0, float(arming_time))
        self.ttl = max(0.0, float(ttl))
        self.payload = payload or {}
        self.owner = owner
        self.spell_key = spell_key
        self.start_time = now
        self.armed_at = now + self.arming_time
        self.exploded = False
