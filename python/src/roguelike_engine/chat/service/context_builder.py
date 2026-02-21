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
        # Cargar memoria para persistencia de idioma por NPC
        mem = self.memory.load(npc_id) if npc_id else None
        stored_code = (mem.preferred_language if mem else "") or ""
        if stored_code not in {"es", "en"}:
            stored_code = "es"
        # Elegir idioma objetivo: preferencia persistida > por defecto 'es' (no detectar)
        target_code = stored_code or "es"
        # Solo soportamos 'es' y 'en' desde el selector
        target_name = "español" if target_code == "es" else "inglés"
        # No actualizamos memoria aquí a partir de detección; solo UI la cambia
        # System base
        sys_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "system.txt")
        safety_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "safety.txt")
        # Estilo básico común
        base_style_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "style_basic.txt")
        # Estilo por rol: primero buscar en style_rol/style_{role}.txt, fallback a legacy style_{role}.txt
        style_role_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "style_rol" / f"style_{role}.txt")
        if not style_role_txt:
            style_role_txt = self._read_text(self.root / "data" / "chat" / "prompts" / f"style_{role}.txt")
        # Estilo por subrol (ej.: style_rol/style_vendor_alchemist.txt) inferido desde persona_id
        subrole_style_txt = ""
        try:
            subrole = None
            if persona_id:
                parts = str(persona_id).split("_")
                if len(parts) >= 2:
                    # vendor_alchemist_valeria -> subrol=alchemist
                    subrole = parts[1]
            if subrole:
                subrole_style_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "style_rol" / f"style_{role}_{subrole}.txt")
        except Exception:
            subrole_style_txt = ""
        # Estilo por persona concreta
        persona_style_txt = self._read_text(self.root / "data" / "chat" / "prompts" / "style_persona" / f"{persona_id}.txt")
        persona = self._read_json(self.root / "data" / "chat" / "personas" / f"{persona_id}.json")
        role_cfg = self._read_json(self.root / "data" / "chat" / "roles" / f"{role}.json")

        system_parts: List[str] = []
        if sys_txt:
            system_parts.append(sys_txt.strip())
        if safety_txt:
            system_parts.append("Seguridad:\n" + safety_txt.strip())
        if base_style_txt:
            system_parts.append("Estilo básico:\n" + base_style_txt.strip())
        if style_role_txt:
            system_parts.append("Estilo de rol:\n" + style_role_txt.strip())
        if subrole_style_txt:
            system_parts.append("Estilo de subrol:\n" + subrole_style_txt.strip())
        if persona_style_txt:
            system_parts.append("Estilo de persona:\n" + persona_style_txt.strip())
        if persona:
            system_parts.append(
                "Persona:\n"
                + json.dumps({k: v for k, v in persona.items() if k not in {"humor", "style"}}, ensure_ascii=False)
            )
            if persona.get("style"):
                system_parts.append("Style:\n" + json.dumps(persona["style"], ensure_ascii=False))
            if persona.get("humor"):
                system_parts.append("Humor:\n" + json.dumps(persona["humor"], ensure_ascii=False))
            # Campos extendidos de personalidad (opcionales)
            if persona.get("traits"):
                system_parts.append("Traits:\n" + json.dumps(persona["traits"], ensure_ascii=False))
            if persona.get("speech"):
                system_parts.append("Speech:\n" + json.dumps(persona["speech"], ensure_ascii=False))
            if persona.get("boundaries"):
                system_parts.append("Boundaries:\n" + json.dumps(persona["boundaries"], ensure_ascii=False))
            if persona.get("knowledge"):
                system_parts.append("Knowledge:\n" + json.dumps(persona["knowledge"], ensure_ascii=False))
            if persona.get("moods"):
                system_parts.append("Moods:\n" + json.dumps(persona["moods"], ensure_ascii=False))
            if persona.get("negotiation"):
                system_parts.append("Negotiation:\n" + json.dumps(persona["negotiation"], ensure_ascii=False))
            if persona.get("smalltalk"):
                system_parts.append("Smalltalk:\n" + json.dumps(persona["smalltalk"], ensure_ascii=False))
        if role_cfg:
            system_parts.append("Role:\n" + json.dumps(role_cfg, ensure_ascii=False))

        # Preferencia de idioma dinámica y prioritaria (persistente por NPC)
        emoji_allowed = False
        emoji_palette: list[str] = []
        try:
            if persona:
                st = persona.get("style") or {}
                emoji_allowed = bool(st.get("emoji", False))
                sp = persona.get("speech") or {}
                if isinstance(sp.get("emoji_palette"), list):
                    emoji_palette = [str(x) for x in sp.get("emoji_palette")]
        except Exception:
            pass
        lang_header_parts = [
            "Idioma (prioritario absoluto):",
            f"Idioma objetivo: {target_name} (código: {target_code}).",
            "RESPONDE EXCLUSIVAMENTE en este idioma, INDEPENDIENTEMENTE del idioma del mensaje del usuario.",
            "No mezcles idiomas, no incluyas traducciones ni frases bilingües salvo petición explícita.",
        ]
        if emoji_allowed:
            if emoji_palette:
                lang_header_parts.append(
                    "Puedes usar emojis de forma moderada y natural. Limítate a esta paleta: "
                    + json.dumps(emoji_palette, ensure_ascii=False)
                )
            else:
                lang_header_parts.append("Puedes usar emojis de forma moderada y natural cuando el tono lo sugiera.")
        else:
            lang_header_parts.append("No uses emojis, emoticonos, pictogramas ni iconos.")
        lang_header = "\n".join(lang_header_parts)
        # Insertar al inicio para máxima precedencia frente a otros prompts de sistema
        system_parts.insert(0, lang_header)
        if target_code in {"es", "en"}:
            system_parts.append(
                f"Preferencia de idioma (prioritaria):\n"
                f"Responde EXCLUSIVAMENTE en {target_name}. No mezcles idiomas, no incluyas traducciones ni frases bilingües. "
                f"Esta preferencia prevalece sobre persona, estilo y cualquier configuración previa."
            )

        # Memory (friendship level, brief context, greeting policy)
        if npc_id:
            mem = self.memory.load(npc_id)
            system_parts.append(
                "Memory:\n" + json.dumps({
                    "friendship_score": mem.friendship_score,
                    "friendship_level": _friendship_level(mem.friendship_score),
                    "has_greeted": bool(getattr(mem, 'has_greeted', False)),
                    "visit_count": int(getattr(mem, 'visit_count', 0) or 0),
                }, ensure_ascii=False)
            )
            # Política de presentación basada en memoria
            if bool(getattr(mem, 'has_greeted', False)):
                system_parts.append(
                    "Política de saludo/presentación:\n"
                    "No te presentes ni saludes formalmente de nuevo. Sé directo y ve al grano. "
                    "Solo vuelve a presentarte si el jugador lo pide explícitamente (p. ej., '¿quién eres?', 'preséntate')."
                )
            else:
                system_parts.append(
                    "Política de saludo/presentación (primera vez):\n"
                    "En esta primera interacción, saluda brevemente y preséntate en 1 línea. "
                    "A partir de la siguiente interacción, no te presentes a menos que el jugador lo pida expresamente."
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
            # Refuerzo: si el usuario pidió explícitamente presentación, habilitarla aunque ya se haya presentado antes
            if _user_requests_intro(user_text):
                msgs.append(LLMMessage(
                    role="system",
                    content=(
                        "El jugador ha solicitado explícitamente tu presentación. "
                        "Preséntate brevemente (1 línea) y continúa con la ayuda solicitada."
                    )
                ))
            # Mensaje de sistema final para reforzar el idioma del turno
            if target_code == "en":
                msgs.append(LLMMessage(
                    role="system",
                    content=(
                        "Language enforcement: Answer this user's last message exclusively in English. "
                        "Do not mix languages, do not include translations or bilingual phrasing."
                    )
                ))
            elif target_code == "es":
                msgs.append(LLMMessage(
                    role="system",
                    content=(
                        "Enforzamiento de idioma: Responde este último mensaje del usuario exclusivamente en español. "
                        "No mezcles idiomas, no incluyas traducciones ni frases bilingües."
                    )
                ))
            

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

def _user_requests_intro(text: str) -> bool:
    t = (text or "").strip().lower()
    if not t:
        return False
    patterns = [
        "quien eres", "quién eres", "quien sos", "quién sos", "presentate", "preséntate",
        "who are you", "introduce yourself", "your name", "what's your name", "whats your name",
    ]
    return any(p in t for p in patterns)
