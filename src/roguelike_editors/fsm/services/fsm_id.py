"""ID helpers (skeleton)."""
from __future__ import annotations


def new_id(prefix: str, existing: set[str]) -> str:
    i = 1
    while True:
        candidate = f"{prefix}_{i}"
        if candidate not in existing:
            return candidate
        i += 1


__all__ = ["new_id"]
