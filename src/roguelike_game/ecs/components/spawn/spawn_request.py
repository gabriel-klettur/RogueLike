from dataclasses import dataclass

@dataclass
class SpawnRequest:
    """Componente que solicita creación de un NPC en una posición dada."""
    prototype: str
    position: tuple[int, int]
