from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List

from ..providers.base import LLMMessage
from .safety import sanitize_text
from .memory_store import MemoryStore


class ContextBuilder:
    """Construye los mensajes para el LLM a partir de datos y mundo.

    Esta versión inicial combina:
    - prompts/system.txt, prompts/safety.txt, prompts/style_vendor.txt (si aplica)
    - persona (data/chat/personas/{persona_id}.json)
    - rol (data/chat/roles/{role}.json)
    - history (lista de pares role/content opcional)
    - user_text (último input del jugador)
    """

    def __init__(self, project_root: Path) -> None:
        self.root = project_root
        self.memory = MemoryStore(self.root)

    def build_messages(
        self,
        role: str,
        persona_id: str,
        history: List[Dict[str, Any]],
        user_text: str,
        npc_id: str | None = None,
    ) -> List[LLMMessage]:
        msgs: List[LLMMessage] = []
        # System base
        sys_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "system.txt")
        safety_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "safety.txt")
        style_role_txt = self._read_text(self.root / "data" / "chat" / "prompts" / f"style_{role}.txt")
        persona = self._read_json(self.root / "data" / "chat" / "personas" / f"{persona_id}.json")
        role_cfg = self._read_json(self.root / "data" / "chat" / "roles" / f"{role}.json")

        system_parts: List[str] = []
        if sys_txt:
            system_parts.append(sys_txt.strip())
        if safety_txt:
            system_parts.append("Seguridad:\n" + safety_txt.strip())
        if style_role_txt:
            system_parts.append("Estilo de rol:\n" + style_role_txt.strip())
        if persona:
            system_parts.append(
                "Persona:\n"
                + json.dumps({k: v for k, v in persona.items() if k not in {"humor", "style"}}, ensure_ascii=False)
            )
            if persona.get("style"):
                system_parts.append("Style:\n" + json.dumps(persona["style"], ensure_ascii=False))
            if persona.get("humor"):
                system_parts.append("Humor:\n" + json.dumps(persona["humor"], ensure_ascii=False))
        if role_cfg:
            system_parts.append("Role:\n" + json.dumps(role_cfg, ensure_ascii=False))

        # Memory (friendship level, brief context)
        if npc_id:
            mem = self.memory.load(npc_id)
            system_parts.append(
                "Memory:\n" + json.dumps({
                    "friendship_score": mem.friendship_score,
                    "friendship_level": _friendship_level(mem.friendship_score),
                }, ensure_ascii=False)
            )

        system_msg = "\n\n".join(system_parts)
        if system_msg:
            msgs.append(LLMMessage(role="system", content=system_msg))

        # Historial previo (incluye ephemeral history si hay npc_id)
        if npc_id:
            mem = self.memory.load(npc_id)
            for h in mem.ephemeral_history or []:
                r = h.get("role", "user")
                c = str(h.get("content", ""))
                if c:
                    msgs.append(LLMMessage(role=r, content=sanitize_text(c)))

        # Historial previo adicional entregado por el llamador
        for h in history or []:
            r = h.get("role", "user")
            c = str(h.get("content", ""))
            if c:
                msgs.append(LLMMessage(role=r, content=sanitize_text(c)))

        # Último input del jugador
        if user_text:
            msgs.append(LLMMessage(role="user", content=sanitize_text(user_text)))

        return msgs

    # --- helpers ---

    def _read_text(self, path: Path) -> str:
        try:
            return path.read_text(encoding="utf-8")
        except Exception:
            return ""

    def _read_json(self, path: Path) -> Dict[str, Any]:
        try:
            with path.open("r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return {}


def _friendship_level(score: int) -> str:
    if score >= 60:
        return "alto"
    if score >= 20:
        return "medio"
    if score <= -40:
        return "muy_bajo"
    return "bajo"
