from __future__ import annotations

import re
from typing import Any, Dict, List, Optional

from .base import LLMProvider, LLMMessage, LLMResult, LLMToolCall


BUY_RE = re.compile(r"\b(?:buy|comprar)\b\s*(\d+)?\s*(wood|wooden|madera)?", re.IGNORECASE)
SELL_RE = re.compile(r"\b(?:sell|vender)\b\s*(\d+)?\s*(wood|wooden|madera)?", re.IGNORECASE)
STOCK_RE = re.compile(r"\b(?:stock|inventario|existencias)\b", re.IGNORECASE)


class DummyProvider(LLMProvider):
    """
    Proveedor offline que intenta detectar comandos sencillos (buy/sell/stock)
    y devuelve tool-calls simulados o texto corto.
    """

    def generate(
        self,
        messages: List[LLMMessage],
        *,
        tools: Optional[List[Dict[str, Any]]] = None,
        tool_choice: str = "auto",
        stream: bool = False,
    ) -> LLMResult:
        user_text = _last_user_text(messages)
        if not user_text:
            return LLMResult(text="¿En qué te puedo ayudar?")

        # Orden de detección: stock, buy, sell
        if STOCK_RE.search(user_text):
            return LLMResult(text="Puedo revisar el stock.", tool_calls=[LLMToolCall(name="vendor.stock", arguments={})])

        m = BUY_RE.search(user_text)
        if m:
            qty = int(m.group(1) or 1)
            item = m.group(2) or "wooden"
            return LLMResult(
                text=f"Entendido, comprar {qty} {item}.",
                tool_calls=[LLMToolCall(name="vendor.buy", arguments={"item": _normalize_item(item), "quantity": qty})],
            )

        m = SELL_RE.search(user_text)
        if m:
            qty = int(m.group(1) or 1)
            item = m.group(2) or "wooden"
            return LLMResult(
                text=f"Entendido, vender {qty} {item}.",
                tool_calls=[LLMToolCall(name="vendor.sell", arguments={"item": _normalize_item(item), "quantity": qty})],
            )

        return LLMResult(text="Puedo ayudarte con 'stock', 'buy <n> wood' o 'sell <n> wood'.")


def _last_user_text(messages: List[LLMMessage]) -> str:
    for msg in reversed(messages):
        if msg.role == "user":
            return msg.content or ""
    return ""


def _normalize_item(token: str) -> str:
    token = (token or "").lower()
    if token in {"wood", "madera", "wooden"}:
        return "wooden"
    return token or "wooden"
