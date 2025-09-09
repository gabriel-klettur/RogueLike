from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional


@dataclass
class ConversationMemory:
    """Memoria por NPC (long-term + ephemeral) simplificada.

    friendship_score: [-100, 100]
    ephemeral_history: últimos K turnos (texto plano por ahora)
    """

    friendship_score: int = 0
    ephemeral_history: List[Dict[str, str]] = None  # [{role, content}]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "friendship_score": self.friendship_score,
            "ephemeral_history": self.ephemeral_history or [],
        }

    @staticmethod
    def from_dict(data: Dict[str, Any]) -> "ConversationMemory":
        cm = ConversationMemory()
        cm.friendship_score = int(data.get("friendship_score", 0))
        cm.ephemeral_history = list(data.get("ephemeral_history", []))
        return cm


class MemoryStore:
    def __init__(self, project_root: Path) -> None:
        self.root = project_root
        self.dir = self.root / "data" / "chat" / "memories"
        self.dir.mkdir(parents=True, exist_ok=True)

    def load(self, entity_id: str) -> ConversationMemory:
        path = self.dir / f"{entity_id}.json"
        if not path.exists():
            return ConversationMemory(friendship_score=0, ephemeral_history=[])
        try:
            with path.open("r", encoding="utf-8") as f:
                data = json.load(f)
            return ConversationMemory.from_dict(data)
        except Exception:
            return ConversationMemory(friendship_score=0, ephemeral_history=[])

    def save(self, entity_id: str, mem: ConversationMemory) -> None:
        path = self.dir / f"{entity_id}.json"
        try:
            with path.open("w", encoding="utf-8") as f:
                json.dump(mem.to_dict(), f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def update_friendship(self, entity_id: str, delta: int) -> int:
        mem = self.load(entity_id)
        mem.friendship_score = int(max(-100, min(100, mem.friendship_score + delta)))
        self.save(entity_id, mem)
        return mem.friendship_score

    def append_ephemeral(self, entity_id: str, role: str, content: str, max_len: int = 12) -> None:
        mem = self.load(entity_id)
        hist = mem.ephemeral_history or []
        hist.append({"role": role, "content": content})
        if len(hist) > max_len:
            hist = hist[-max_len:]
        mem.ephemeral_history = hist
        self.save(entity_id, mem)
