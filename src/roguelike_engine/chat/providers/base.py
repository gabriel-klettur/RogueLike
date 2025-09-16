from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional


@dataclass
class LLMMessage:
    role: str  # 'system' | 'user' | 'assistant'
    content: str


@dataclass
class LLMToolCall:
    name: str
    arguments: Dict[str, Any]


@dataclass
class LLMResult:
    text: str
    tool_calls: Optional[List[LLMToolCall]] = None
    usage: Optional[Dict[str, Any]] = None
    finish_reason: Optional[str] = None


class LLMProvider:
    """Interface para proveedores LLM."""

    def generate(
        self,
        messages: List[LLMMessage],
        *,
        tools: Optional[List[Dict[str, Any]]] = None,
        tool_choice: str = "auto",
        stream: bool = False,
    ) -> LLMResult:
        raise NotImplementedError
