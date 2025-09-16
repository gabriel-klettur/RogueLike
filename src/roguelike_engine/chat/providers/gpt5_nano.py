from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional
from urllib import request, error
import logging
import re

from .base import LLMProvider, LLMMessage, LLMResult, LLMToolCall


@dataclass
class _ProviderConfig:
    api_url: str
    model: str


class Gpt5NanoProvider(LLMProvider):
    """
    Implementación mínima del Responses API usando urllib (sin dependencias externas).

    - Lee OPENAI_API_KEY del entorno.
    - Carga api_url y (opcional) model desde data/config/chat.json. Fallbacks seguros.
    - Mapea nuestros mensajes a `input` del Responses API.
    - Devuelve texto en `LLMResult`. El soporte de tool-calls puede ampliarse más adelante.
    """

    def __init__(self, api_key: Optional[str] = None) -> None:
        # __file__ .../src/roguelike_game/chat/providers/gpt5_nano.py -> parents[4] = repo root
        self.root = Path(__file__).resolve().parents[4]
        # Intentar cargar .env si no hay API key en entorno
        if not os.getenv("OPENAI_API_KEY"):
            self._load_dotenv()
        self.api_key = api_key or os.getenv("OPENAI_API_KEY") or ""
        self.cfg = self._load_cfg()
        try:
            logging.getLogger(__name__).info(
                "[Gpt5NanoProvider] init api_url=%s model=%s key_len=%s",
                self.cfg.api_url,
                self.cfg.model,
                len(self.api_key or ""),
            )
        except Exception:
            pass

    def generate(
        self,
        messages: List[LLMMessage],
        *,
        tools: Optional[List[Dict[str, Any]]] = None,
        tool_choice: str = "auto",
        stream: bool = False,
    ) -> LLMResult:
        if not self.api_key:
            raise RuntimeError("OPENAI_API_KEY no configurada")

        payload: Dict[str, Any] = {
            "model": self.cfg.model,
            "input": [self._map_message(m) for m in messages],
            "reasoning": {"effort": "low"},
            "text": {"verbosity": "low"},
        }
        if tools:
            payload["tools"] = self._map_tools(tools)
            payload["tool_choice"] = "auto"
        if stream:
            payload["stream"] = True

        # Log del payload (truncado)
        try:
            logging.getLogger(__name__).info(
                "[Gpt5NanoProvider] payload: %s",
                json.dumps(payload, ensure_ascii=False)[:2000],
            )
        except Exception:
            pass

        req = request.Request(
            self.cfg.api_url,
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
            },
            method="POST",
        )
        try:
            logging.getLogger(__name__).info(
                "[Gpt5NanoProvider] POST %s model=%s tools=%s msgs=%s",
                self.cfg.api_url,
                self.cfg.model,
                len(tools or []),
                len(messages or []),
            )
            with request.urlopen(req, timeout=20) as resp:
                data = resp.read().decode("utf-8")
                try:
                    logging.getLogger(__name__).info(
                        "[Gpt5NanoProvider] raw_response: %s", data[:2000]
                    )
                except Exception:
                    pass
                obj = json.loads(data)
        except error.HTTPError as e:
            try:
                body = e.read().decode('utf-8', 'ignore')
            except Exception:
                body = ''
            logging.getLogger(__name__).error("[Gpt5NanoProvider] HTTPError code=%s body=%s", getattr(e, 'code', 'ERR'), body)
            raise RuntimeError(f"HTTP {getattr(e, 'code', 'ERR')}: {body}")
        except Exception as e:
            logging.getLogger(__name__).exception("[Gpt5NanoProvider] Error calling Responses API")
            raise RuntimeError(f"Error al llamar Responses API: {e}")

        text = obj.get("output_text") or ""
        tool_calls: List[LLMToolCall] = []
        # Fallback: intentar extraer texto si output_text viene vacío
        if not text:
            try:
                # Algunas variantes devuelven 'output' con bloques; intentar tomar texto
                out = obj.get("output") or []
                if isinstance(out, list) and out:
                    buf = []
                    for it in out:
                        # Caso string directo
                        if isinstance(it, str):
                            buf.append(it)
                            continue
                        if not isinstance(it, dict):
                            continue
                        # Parseo de function_call a tool-calls
                        if it.get("type") == "function_call" and it.get("status") == "completed":
                            name = str(it.get("name") or "").strip()
                            # Mapear vendor_buy -> vendor.buy
                            mapped_name = name.replace("_", ".") if name else name
                            args_raw = it.get("arguments")
                            args_dict: Dict[str, Any] = {}
                            if isinstance(args_raw, str):
                                try:
                                    args_dict = json.loads(args_raw)
                                except Exception:
                                    args_dict = {}
                            elif isinstance(args_raw, dict):
                                args_dict = args_raw
                            if mapped_name:
                                tool_calls.append(LLMToolCall(name=mapped_name, arguments=args_dict))
                        # Caso dict con 'text' directo
                        direct = it.get("text")
                        if isinstance(direct, str):
                            buf.append(direct)
                        # Caso dict con 'content': puede ser lista de bloques
                        content = it.get("content")
                        if isinstance(content, list):
                            for blk in content:
                                if isinstance(blk, dict):
                                    t = blk.get("text")
                                    if isinstance(t, str):
                                        buf.append(t)
                                    # Algunos devuelven type=output_text
                                    if blk.get("type") in {"output_text", "text"} and isinstance(blk.get("content"), str):
                                        buf.append(blk.get("content"))
                    if buf:
                        text = "\n".join([s for s in buf if s])
            except Exception:
                pass
        try:
            logging.getLogger(__name__).info("[Gpt5NanoProvider] ok output_text_len=%s", len(text))
        except Exception:
            pass
        # TODO: parsear tool-calls si el Responses API las retorna explícitamente.
        return LLMResult(text=text, tool_calls=tool_calls or None, usage=obj.get("usage"), finish_reason=obj.get("finish_reason"))

    # --- helpers ---

    def _load_cfg(self) -> _ProviderConfig:
        path = self.root / "data" / "config" / "chat.json"
        api_url = "https://api.openai.com/v1/responses"
        model = os.getenv("CHAT_MODEL") or "gpt-5"
        try:
            with path.open("r", encoding="utf-8") as f:
                obj = json.load(f)
                api_url = obj.get("api_url", api_url)
                model = obj.get("model", model)
        except Exception:
            pass
        return _ProviderConfig(api_url=api_url, model=model)

    def _map_message(self, m: LLMMessage) -> Dict[str, Any]:
        # Responses API acepta `input` como lista de objetos {role, content}
        # donde content puede ser string directo.
        return {"role": m.role, "content": m.content}

    def _map_tools(self, functions: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Mapea definiciones de funciones a herramientas del Responses API.

        Formato esperado (según docs GPT-5): cada tool en la lista debe tener
        campos top-level: type, name, description, parameters.
        """
        tools: List[Dict[str, Any]] = []
        for fn in functions:
            raw_name = fn.get("name")
            desc = fn.get("description", "")
            params = fn.get("parameters", {})
            if not raw_name:
                continue
            # Sanitizar nombre para cumplir ^[a-zA-Z0-9_-]+$
            name = re.sub(r"[^a-zA-Z0-9_-]", "_", str(raw_name))
            if not name:
                # Fallback por si quedara vacío
                name = "tool"
            tools.append({
                "type": "function",
                "name": name,
                "description": desc,
                "parameters": params,
            })
        return tools

    def _load_dotenv(self) -> None:
        """Carga un archivo .env simple (KEY=VALUE) desde la raíz del repo si existe."""
        try:
            path = self.root / ".env"
            if not path.exists():
                return
            for line in path.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                if "=" not in line:
                    continue
                k, v = line.split("=", 1)
                k = k.strip()
                v = v.strip().strip('"').strip("'")
                if k and v and k not in os.environ:
                    os.environ[k] = v
        except Exception:
            # Silencioso: no bloquear por un .env malformado
            pass
