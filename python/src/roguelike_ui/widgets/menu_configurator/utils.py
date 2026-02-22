from __future__ import annotations

import pygame
from typing import Optional


_KEYCODE_CONST_CACHE: Optional[dict[int, str]] = None


def key_const_from_code(key_code: int) -> Optional[str]:
    """Map a pygame key code to its constant name 'K_*'.

    Builds a cache on first use for O(1) lookups.
    """
    global _KEYCODE_CONST_CACHE
    if _KEYCODE_CONST_CACHE is None:
        cache: dict[int, str] = {}
        for name in dir(pygame):
            if not name.startswith("K_"):
                continue
            try:
                val = getattr(pygame, name)
            except Exception:
                continue
            if isinstance(val, int):
                cache[val] = name
        _KEYCODE_CONST_CACHE = cache
    return _KEYCODE_CONST_CACHE.get(key_code)


def format_action_name(action: str) -> str:
    """Return a user-friendly action name.

    Removes the 'mouse_' prefix for display and title-cases words.
    """
    name = action
    if isinstance(name, str) and name.startswith("mouse_"):
        name = name[len("mouse_"):]
    return name.replace("_", " ").title()


def format_action_friendly(action: str, slot_hint: str | None = None) -> str:
    """Return a friendly display name for an action, including its channel.

    Examples:
    - kb_dash_a -> "Dash (Teclado A)"
    - kb_dash_b -> "Dash (Teclado B)"
    - mouse_dash -> "Dash (Ratón)"
    - interact + slot_hint='keyboard_a' -> "Interact (Teclado)"
    - interact + slot_hint='mouse' -> "Interact (Ratón)"
    """
    if not isinstance(action, str):
        return str(action)

    if action.startswith("kb_"):
        body = action[len("kb_"):]
        base = body[:-2] if body.endswith("_a") or body.endswith("_b") else body
        nice = base.replace("_", " ").title()
        if body.endswith("_a") or (slot_hint == "keyboard_a"):
            return f"{nice} (Teclado A)"
        if body.endswith("_b") or (slot_hint == "keyboard_b"):
            return f"{nice} (Teclado B)"
        return f"{nice} (Teclado)"

    if action.startswith("mouse_"):
        base = action[len("mouse_"):]
        nice = base.replace("_", " ").title()
        return f"{nice} (Ratón)"

    nice = action.replace("_", " ").title()
    if slot_hint in {"keyboard_a", "keyboard_b", "keyboard"}:
        return f"{nice} (Teclado)"
    if slot_hint == "mouse":
        return f"{nice} (Ratón)"
    return nice
