from __future__ import annotations

import pygame
from typing import List, Dict, Any

from ..ecs.events import ChatPlayerMessage
from .chat_overlay_controller import ChatOverlayController


class ChatOverlayEvents:
    def __init__(
        self,
        controller: ChatOverlayController,
        chat_system,
        *,
        player_id: int,
        npc_id: int,
        role: str,
        persona_id: str,
        history: List[Dict[str, Any]],
    ) -> None:
        self.controller = controller
        self.chat_system = chat_system
        self.player_id = player_id
        self.npc_id = npc_id
        self.role = role
        self.persona_id = persona_id
        self.history = history

    def handle_event(self, ev: pygame.event.Event) -> bool:
        if not self.controller.state.visible:
            return False

        if ev.type == pygame.KEYDOWN:
            if ev.key == pygame.K_ESCAPE:
                self.controller.hide()
                return True
            if ev.key == pygame.K_RETURN:
                text = self.controller.state.input_buffer.strip()
                if text:
                    # enviar al sistema
                    self.chat_system.handle_player_message(
                        ChatPlayerMessage(player_id=self.player_id, npc_id=self.npc_id, text=text),
                        role=self.role,
                        persona_id=self.persona_id,
                        history=self.history,
                    )
                    self.controller.append_message("you", text)
                    self.controller.state.input_buffer = ""
                    self.controller.set_typing(True)
                return True
            if ev.key == pygame.K_BACKSPACE:
                self.controller.state.input_buffer = self.controller.state.input_buffer[:-1]
                return True
            # Añadir caracteres imprimibles sencillos (ASCII)
            ch = ev.unicode
            if ch and ch.isprintable():
                self.controller.state.input_buffer += ch
                return True
        return False
