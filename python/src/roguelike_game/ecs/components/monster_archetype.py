from dataclasses import dataclass

@dataclass
class MonsterArchetype:
    """
    Componente que guarda el identificador de clase/prototipo del monstruo
    usado por la fábrica al crearlo (p.ej., "goblin", "vendor_blacksmith").
    """
    type: str
