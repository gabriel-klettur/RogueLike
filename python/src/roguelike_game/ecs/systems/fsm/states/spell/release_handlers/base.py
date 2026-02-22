"""Base classes for spell release handlers."""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any, MutableMapping, Protocol


class SupportsSpellContext(Protocol):
    """Minimal context contract shared across handlers."""

    spell_type: str
    spell_key: str
    spell_cfg: MutableMapping[str, Any]
    context: MutableMapping[str, Any]
    entity: Any
    world: Any
    camera: Any


class SpellReleaseHandler(ABC):
    """Contract for every spell release handler implementation."""

    @abstractmethod
    def handle(self, context: SupportsSpellContext) -> None:
        """Execute the release behaviour for the provided spell."""

