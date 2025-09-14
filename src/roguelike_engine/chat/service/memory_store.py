from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List


@dataclass
class ConversationMemory:
    """Memoria por NPC (long-term + ephemeral) simplificada.

    friendship_score: [-100, 100]
    ephemeral_history: últimos K turnos (texto plano por ahora)
    """

    friendship_score: int = 0
    ephemeral_history: List[Dict[str, str]] = None  # [{role, content}]
    preferred_language: str = ""  # 'es', 'en', etc. (opcional)
    has_greeted: bool = False  # True si ya se presentó en alguna ocasión

    def to_dict(self) -> Dict[str, Any]:
        return {
            "friendship_score": self.friendship_score,
            "ephemeral_history": self.ephemeral_history or [],
            "preferred_language": self.preferred_language or "",
            "has_greeted": bool(self.has_greeted),
        }

    @staticmethod
    def from_dict(data: Dict[str, Any]) -> "ConversationMemory":
        cm = ConversationMemory()
        cm.friendship_score = int(data.get("friendship_score", 0))
        cm.ephemeral_history = list(data.get("ephemeral_history", []))
        cm.preferred_language = str(data.get("preferred_language", "") or "")
        try:
            cm.has_greeted = bool(data.get("has_greeted", False))
        except Exception:
            cm.has_greeted = False
        return cm


class MemoryStore:
    def __init__(self, project_root: Path) -> None:
        self.root = project_root
        self.dir = self.root / "data" / "chat" / "memories"
        self.dir.mkdir(parents=True, exist_ok=True)

    # --- Path helpers (new layout only) -------------------------------------
    def _npc_dir(self, entity_id: str, ensure: bool = False) -> Path:
        """Directory for a specific NPC/entity where all its memories live.

        Layout: data/chat/memories/<entity_id>/memory.json
        """
        d = self.dir / str(entity_id)
        if ensure:
            d.mkdir(parents=True, exist_ok=True)
        return d

    def _memory_path(self, entity_id: str, ensure_dir: bool = False) -> Path:
        d = self._npc_dir(entity_id, ensure=ensure_dir)
        return d / "memory.json"

    def load(self, entity_id: str) -> ConversationMemory:
        """Load memory for entity using the new folder layout only."""
        npath = self._memory_path(entity_id, ensure_dir=False)
        if npath.exists():
            try:
                with npath.open("r", encoding="utf-8") as f:
                    data = json.load(f)
                return ConversationMemory.from_dict(data)
            except Exception:
                return ConversationMemory(friendship_score=0, ephemeral_history=[])
        return ConversationMemory(friendship_score=0, ephemeral_history=[])

    def save(self, entity_id: str, mem: ConversationMemory) -> None:
        """Save memory using the new folder layout only."""
        path = self._memory_path(entity_id, ensure_dir=True)
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

    # --- Language helpers ---
    def set_language(self, entity_id: str, lang_code: str) -> None:
        mem = self.load(entity_id)
        lc = (lang_code or "").strip().lower()
        if lc not in {"es", "en"}:
            lc = "es"
        mem.preferred_language = lc
        self.save(entity_id, mem)

    def get_language(self, entity_id: str) -> str:
        mem = self.load(entity_id)
        return (mem.preferred_language or "").strip().lower()

    # --- Greeting helpers ---
    def has_greeted_flag(self, entity_id: str) -> bool:
        try:
            mem = self.load(entity_id)
            return bool(getattr(mem, 'has_greeted', False))
        except Exception:
            return False

    def mark_greeted(self, entity_id: str) -> None:
        mem = self.load(entity_id)
        mem.has_greeted = True
        self.save(entity_id, mem)
