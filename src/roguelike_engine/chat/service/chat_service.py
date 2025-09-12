from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional
import re
import logging

from ..providers.base import LLMMessage, LLMProvider, LLMResult, LLMToolCall
from .safety import sanitize_text
from ..providers.dummy import DummyProvider
from .context_builder import ContextBuilder
from .tool_registry import ToolRegistry, ToolExecResult

logger = logging.getLogger(__name__)

@dataclass
class ChatJob:
    player_id: int
    npc_id: int
    user_text: str
    role: str
    persona_id: str
    history: List[Dict[str, Any]]


@dataclass
class ChatResult:
    text: str
    effects: Dict[str, Any]
    tool_calls: List[LLMToolCall]
    provider: str
    offline: bool


class ChatService:
    """Orquestador del flujo de chat.

    - Carga provider desde data/config/chat.json.
    - Construye mensajes (system/safety/style/persona) con ContextBuilder.
    - Pasa herramientas (functions) al provider y ejecuta tool-calls con ToolRegistry.
    - Tiene fallback offline usando DummyProvider.
    """

    def __init__(self, project_root: Optional[Path] = None) -> None:
        # Resolver raíz del repo buscando un directorio que contenga data/config/chat.json
        self.root = self._resolve_root(project_root)
        try:
            logger.info("[ChatService] Resolved repo root=%s", str(self.root))
        except Exception:
            pass
        self.config = self._load_config()
        self.provider_name = str(self.config.get("provider", "dummy")).lower()
        self.provider = self._create_provider(self.provider_name)
        self.registry = ToolRegistry()
        self.ctx_builder = ContextBuilder(self.root)
        self.tools_spec = self._load_tools_spec()
        try:
            logger.info("[ChatService] Config provider=%s model=%s", self.provider_name, self.config.get("model"))
        except Exception:
            pass

    # --- Public API ---

    def process(self, job: ChatJob) -> ChatResult:
        messages = self.ctx_builder.build_messages(
            role=job.role,
            persona_id=job.persona_id,
            history=job.history,
            user_text=job.user_text,
            npc_id=str(job.npc_id),
        )
        offline = False
        # Determinar idioma objetivo para este turno (persistente por NPC)
        npc_key = str(job.npc_id)
        target_code = self._target_language(npc_key, job.user_text or "")

        try:
            logger.debug("[ChatService] Calling provider=%s offline_pre=%s", self.provider_name, offline)
            if self.provider_name == "dummy":
                offline = True
                llm_res = self.provider.generate(messages, tools=self.tools_spec)
            else:
                # Intento online
                llm_res = self.provider.generate(messages, tools=self.tools_spec)
        except Exception as e:
            # Fallback offline
            offline = True
            try:
                logger.exception("[ChatService] Provider '%s' failed, fallback to Dummy: %s", self.provider_name, e)
            except Exception:
                pass
            dummy = DummyProvider()
            llm_res = dummy.generate(messages, tools=self.tools_spec)

        text, effects, calls = self._handle_result(llm_res)
        # Si la respuesta textual no coincide con el idioma objetivo y no estamos offline, realizar hasta dos reintentos
        try:
            res_code = self._detect_language(text or "")
            if not offline and res_code and target_code and res_code != target_code:
                # Reintento 1: añadir enforcement a la conversación actual
                enforce = (
                    "Language enforcement: Answer this user's last message exclusively in English. "
                    "Do not mix languages, do not include translations or bilingual phrasing."
                    if target_code == "en"
                    else
                    "Enforzamiento de idioma: Responde este último mensaje del usuario exclusivamente en español. "
                    "No mezcles idiomas, no incluyas traducciones ni frases bilingües."
                )
                messages_retry_1 = list(messages) + [LLMMessage(role="system", content=enforce)]
                llm_res2 = self.provider.generate(messages_retry_1, tools=self.tools_spec)
                text2, effects2, calls2 = self._handle_result(llm_res2)
                res_code2 = self._detect_language(text2 or "")
                if res_code2 == target_code:
                    text, effects, calls = text2, effects2, calls2
                else:
                    # Reintento 2: conversación mínima (solo enforcement + último mensaje del usuario)
                    minimal_enforce = (
                        "Language enforcement: Answer exclusively in English. Do not mix languages or include translations."
                        if target_code == "en"
                        else
                        "Enforzamiento de idioma: Responde exclusivamente en español. No mezcles idiomas ni incluyas traducciones."
                    )
                    minimal_msgs = [
                        LLMMessage(role="system", content=minimal_enforce),
                        LLMMessage(role="user", content=job.user_text or ""),
                    ]
                    llm_res3 = self.provider.generate(minimal_msgs, tools=self.tools_spec)
                    text3, effects3, calls3 = self._handle_result(llm_res3)
                    res_code3 = self._detect_language(text3 or "")
                    if res_code3 != target_code:
                        # Reintento 3: reescritura del texto previo al idioma objetivo
                        if target_code == "en":
                            rewrite_sys = "Rewrite the following text into English only. Do not add or remove meaning. Do not include any translation or bilingual phrasing."
                        else:
                            rewrite_sys = "Reescribe el siguiente texto en español únicamente. No agregues ni quites significado. No incluyas traducciones ni frases bilingües."
                        rewrite_msgs = [
                            LLMMessage(role="system", content=rewrite_sys),
                            LLMMessage(role="user", content=(text3 or text2 or text or "")),
                        ]
                        llm_res4 = self.provider.generate(rewrite_msgs, tools=self.tools_spec)
                        text4, effects4, calls4 = self._handle_result(llm_res4)
                        text, effects, calls = text4 or text3 or text2 or text, effects4 or effects3 or effects2 or effects, calls4 or calls3 or calls2 or calls
                    else:
                        # Preferir el último intento válido
                        text, effects, calls = text3 or text2 or text, effects3 or effects2 or effects, calls3 or calls2 or calls
        except Exception:
            pass
        return ChatResult(text=text, effects=effects, tool_calls=calls or [], provider=self.provider_name, offline=offline)

    # --- Internals ---

    def _load_config(self) -> Dict[str, Any]:
        cfg_path = self.root / "data" / "config" / "chat.json"
        try:
            logger.info("[ChatService] Loading config at path=%s exists=%s", str(cfg_path), cfg_path.exists())
        except Exception:
            pass
        if not cfg_path.exists():
            return {"provider": "dummy"}
        try:
            with cfg_path.open("r", encoding="utf-8") as f:
                obj = json.load(f)
            try:
                logger.info("[ChatService] Loaded config provider=%s model=%s", obj.get("provider"), obj.get("model"))
            except Exception:
                pass
            return obj
        except Exception as e:
            try:
                logger.exception("[ChatService] Error reading config: %s", e)
            except Exception:
                pass
            return {"provider": "dummy"}

    def _resolve_root(self, project_root: Optional[Path]) -> Path:
        """Resuelve la raíz del repositorio buscando un directorio que contenga data/config/chat.json.

        Prioriza:
        1) project_root explícito
        2) Análisis ascendente desde este archivo (__file__)
        3) CWD y sus padres
        4) Fallback a parents[4] (estructura esperada src/roguelike_game/...)
        """
        if project_root:
            return Path(project_root)
        candidates: list[Path] = []
        try:
            here = Path(__file__).resolve()
            candidates.extend([p for p in here.parents])
        except Exception:
            pass
        try:
            cwd = Path.cwd().resolve()
            candidates.append(cwd)
            candidates.extend([p for p in cwd.parents])
        except Exception:
            pass
        for base in candidates:
            cfg = base / "data" / "config" / "chat.json"
            if cfg.exists():
                return base
        try:
            return Path(__file__).resolve().parents[4]
        except Exception:
            return Path('.')

    def _create_provider(self, name: str) -> LLMProvider:
        if name == "dummy":
            try:
                logger.info("[ChatService] Creating provider=DUMMY")
            except Exception:
                pass
            return DummyProvider()
        if name in {"gpt5-nano", "gpt-5", "gpt5"}:
            try:
                from ..providers.gpt5_nano import Gpt5NanoProvider  # type: ignore
            except Exception:
                # Si no está implementado aún, usar Dummy
                try:
                    logger.warning("[ChatService] Provider '%s' not available, falling back to Dummy", name)
                except Exception:
                    pass
                return DummyProvider()
            try:
                logger.info("[ChatService] Creating provider=Gpt5NanoProvider")
            except Exception:
                pass
            return Gpt5NanoProvider(api_key=os.getenv("OPENAI_API_KEY"))
        # Desconocido -> Dummy
        try:
            logger.warning("[ChatService] Unknown provider name=%s -> Dummy", name)
        except Exception:
            pass
        return DummyProvider()

    def _load_tools_spec(self) -> List[Dict[str, Any]]:
        tools: List[Dict[str, Any]] = []
        tools_dir = self.root / "data" / "chat" / "tools"
        if tools_dir.exists():
            for p in tools_dir.glob("*.schema.json"):
                try:
                    with p.open("r", encoding="utf-8") as f:
                        data = json.load(f)
                    # Esperamos {"functions": [...]} según documentación
                    for fn in data.get("functions", []) or []:
                        tools.append(fn)
                except Exception:
                    continue
        return tools

    def _handle_result(self, res: LLMResult) -> tuple[str, Dict[str, Any], List[LLMToolCall]]:
        # Si hay tool-calls, ejecutarlas y construir respuesta
        calls: List[LLMToolCall] = []
        if res.tool_calls:
            merged_effects: Dict[str, Any] = {}
            last_msg = sanitize_text(res.text or "")
            for call in res.tool_calls:
                calls.append(call)
                tr: ToolExecResult = self.registry.execute(call.name, call.arguments)
                # Sanear mensaje de tool si aporta texto
                msg = sanitize_text(tr.message or "")
                last_msg = msg or last_msg
                # Merge naive de efectos
                for k, v in (tr.effects or {}).items():
                    if k not in merged_effects:
                        merged_effects[k] = v
                    else:
                        # Si son dicts con cantidades, sumar
                        if isinstance(merged_effects[k], dict) and isinstance(v, dict):
                            for ik, iv in v.items():
                                merged_effects[k][ik] = merged_effects[k].get(ik, 0) + iv
                        else:
                            merged_effects[k] = v
            return sanitize_text(last_msg) or sanitize_text(res.text or ""), merged_effects, calls
        # Sin tools: devolver texto simple
        return sanitize_text(res.text or ""), {}, calls

    # --- Idioma helpers (solo verificación/enforcement; el objetivo lo define el selector) ---
    def _detect_language(self, text: str) -> str:
        t = (text or "").strip().lower()
        if not t:
            return "es"
        if re.search(r"[áéíóúñü]", t):
            return "es"
        toks = re.findall(r"[a-zA-Z']+", t)
        es_sw = {"hola","qué","que","como","cómo","de","la","el","y","por","para","gracias","quiero","comprar","vender","madera"}
        en_sw = {"the","and","you","hi","hello","please","would","like","about","to","buy","sell","wood"}
        es_count = sum(1 for w in toks if w in es_sw)
        en_count = sum(1 for w in toks if w in en_sw)
        return "en" if en_count > es_count else "es"

    def _target_language(self, npc_id: str, user_text: str) -> str:
        """Idioma objetivo puramente dirigido por preferencia UI.

        - Si hay preferencia persistida para el NPC, usarla.
        - Si no, fallback a 'es'.
        """
        try:
            stored = self.ctx_builder.memory.get_language(npc_id)
        except Exception:
            stored = ""
        return stored or "es"
