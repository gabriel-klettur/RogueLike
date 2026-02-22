from dataclasses import dataclass

@dataclass
class DamageConfig:
    """
    Configuración de duración de daño para NPCs.
    """
    duration: float
    # Probabilidad de quedarse quieto (stun) al recibir daño. Si es 0, nunca se detiene; si es 1, siempre se detiene.
    stop_probability: float = 0.25