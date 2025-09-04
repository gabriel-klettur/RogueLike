from dataclasses import dataclass, field
from typing import List, Tuple


@dataclass
class ChatBubble:
    text: str
    created_ms: int
    ttl_ms: int = 2500  # duración total en ms
    color: Tuple[int, int, int] = (255, 255, 255)
    bg_color: Tuple[int, int, int] = (20, 20, 20)
    outline_color: Tuple[int, int, int] = (255, 255, 255)


@dataclass
class FloatingChatBubbleComponent:
    """
    Lista de burbujas de chat activas que deben renderizarse sobre esta entidad.
    Cada burbuja se desvanece automáticamente con el tiempo (ttl_ms).
    """
    bubbles: List[ChatBubble] = field(default_factory=list)
