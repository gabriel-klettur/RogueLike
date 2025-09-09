from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional


# Requests provenientes de input o lógica de juego
@dataclass
class ChatOpenRequest:
    player_id: int
    npc_id: int


@dataclass
class ChatPlayerMessage:
    player_id: int
    npc_id: int
    text: str


@dataclass
class ChatCloseRequest:
    player_id: int
    npc_id: int
    reason: str = "user"


# Job interno para procesar con ChatService
@dataclass
class ChatServiceJob:
    player_id: int
    npc_id: int
    role: str
    persona_id: str
    history: List[Dict[str, Any]]
    user_text: str


# Resultado del ChatService para consumir por UI/world
@dataclass
class ChatResponseEvent:
    player_id: int
    npc_id: int
    text: str
    effects: Dict[str, Any]
    meta: Optional[Dict[str, Any]] = None
