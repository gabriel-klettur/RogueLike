from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class ChatComponent:
    """
    Componente ECS para habilitar chat sobre una entidad.

    - chat_range: distancia máxima (en unidades del juego) para iniciar chat.
    - role: rol del chat para enrutamiento (e.g., 'vendor', 'generic', 'quest_giver').

    Nota: El estado de UI del chat (abierto, buffer de texto, mensajes mostrados) se
    maneja de forma global en GameState para coordinar bloqueo de input y renderizado.
    Este componente únicamente declara capacidades y parámetros por entidad.
    """
    chat_range: float = 10.0
    role: str = "generic"
    greeting: Optional[str] = None
    recent_messages: List[str] = field(default_factory=list)
