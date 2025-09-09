from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

from .events import (
    ChatOpenRequest,
    ChatPlayerMessage,
    ChatCloseRequest,
    ChatServiceJob,
    ChatResponseEvent,
)
from ..service.chat_service import ChatService, ChatJob


@dataclass
class ChatQueues:
    open_requests: List[ChatOpenRequest] = field(default_factory=list)
    player_messages: List[ChatPlayerMessage] = field(default_factory=list)
    close_requests: List[ChatCloseRequest] = field(default_factory=list)
    jobs: List[ChatServiceJob] = field(default_factory=list)
    responses: List[ChatResponseEvent] = field(default_factory=list)


class ChatSystem:
    """
    Sistema de alto nivel para gestionar apertura/cierre y encolado de mensajes.

    NOTA: Esta implementación es un stub independiente (no registrado todavía en el
    `system_registry`). El objetivo es ilustrar el flujo y permitir tests locales.
    """

    def __init__(self, queues: Optional[ChatQueues] = None) -> None:
        self.q = queues or ChatQueues()

    def handle_open(self, req: ChatOpenRequest) -> None:
        self.q.open_requests.append(req)

    def handle_player_message(self, msg: ChatPlayerMessage, role: str, persona_id: str, history: List[Dict[str, Any]]) -> None:
        job = ChatServiceJob(
            player_id=msg.player_id,
            npc_id=msg.npc_id,
            role=role,
            persona_id=persona_id,
            history=history,
            user_text=msg.text,
        )
        self.q.jobs.append(job)

    def handle_close(self, req: ChatCloseRequest) -> None:
        self.q.close_requests.append(req)


class ChatProcessingSystem:
    """Saca jobs de la cola, llama a ChatService y publica respuestas.

    En una integración real, esto debería correr en background (thread pool) para
    no bloquear el frame loop. Aquí lo hacemos sin concurrencia para simplicidad.
    """

    def __init__(self, queues: Optional[ChatQueues] = None, service: Optional[ChatService] = None) -> None:
        self.q = queues or ChatQueues()
        self.service = service or ChatService()

    def process_pending(self) -> int:
        count = 0
        while self.q.jobs:
            job = self.q.jobs.pop(0)
            result = self.service.process(
                ChatJob(
                    player_id=job.player_id,
                    npc_id=job.npc_id,
                    user_text=job.user_text,
                    role=job.role,
                    persona_id=job.persona_id,
                    history=job.history,
                )
            )
            self.q.responses.append(
                ChatResponseEvent(
                    player_id=job.player_id,
                    npc_id=job.npc_id,
                    text=result.text,
                    effects=result.effects,
                    meta={"provider": result.provider, "offline": result.offline},
                )
            )
            count += 1
        return count
