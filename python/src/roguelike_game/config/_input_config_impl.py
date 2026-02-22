from __future__ import annotations
import json
import os
from typing import Optional

import pygame

from ._input_defaults import DEFAULT_BINDINGS, MOUSE_DEFAULTS, TRISLOT_BASES


class InputConfig:
    """
    Input configuration loader and resolver with per-path singleton semantics.

    Preserves the public API used across the codebase:
    - __init__(path=None)
    - _load()
    - get_key(action)
    - get_keys_for_action(action)
    - set_key(action, keyname)
    - set_binding(binding_key, value, enforce_unique=True)
    - get_key_for_binding(binding_key)
    - get_mouse_button_for_binding(binding_key)
    - get_mouse_button(action)
    - save()
    """

    _instances: dict[str, "InputConfig"] = {}

    def __new__(cls, path: Optional[str] = None):
        resolved = path or os.path.join(os.getcwd(), "data", "config", "input_bindings.json")
        if resolved in cls._instances:
            return cls._instances[resolved]
        inst = super().__new__(cls)
        cls._instances[resolved] = inst
        return inst

    def __init__(self, path: Optional[str] = None):
        if getattr(self, "_initialized", False):
            return
        self._initialized = True
        self.path = path or os.path.join(os.getcwd(), "data", "config", "input_bindings.json")
        self.bindings: dict[str, str] = {}
        self._load()

    # -------------------------------------
    # Persistence & bootstrap
    # -------------------------------------
    def _load(self) -> None:
        fresh = False
        if os.path.exists(self.path):
            try:
                with open(self.path, "r", encoding="utf-8") as f:
                    self.bindings = json.load(f) or {}
            except Exception:
                # Corrupt or invalid -> reset to empty defaults
                self.bindings = {}
        else:
            self.bindings = {}
            fresh = True
        # Ensure directories exist for first write
        os.makedirs(os.path.dirname(self.path), exist_ok=True)

        # Ensure default keyboard and mouse bindings for discoverability
        ensured = False
        for k, v in DEFAULT_BINDINGS.items():
            if k not in self.bindings:
                self.bindings[k] = v
                ensured = True
        for k, v in MOUSE_DEFAULTS.items():
            if k not in self.bindings:
                self.bindings[k] = v
                ensured = True

        # Tri-slot keyboard spaces (kb_<base>_a/b) default to empty (unbound)
        for base in TRISLOT_BASES:
            a = f"kb_{base}_a"
            b = f"kb_{base}_b"
            if a not in self.bindings:
                self.bindings[a] = ""
                ensured = True
            if b not in self.bindings:
                self.bindings[b] = ""
                ensured = True

        # Migration: old reload_data K_F5 -> K_F1
        try:
            if str(self.bindings.get("reload_data", "")).upper() == "K_F5":
                self.bindings["reload_data"] = "K_F1"
                ensured = True
        except Exception:
            pass

        # Save only when needed (fresh or ensured new keys)
        if fresh or ensured:
            self.save()

    def save(self) -> None:
        with open(self.path, "w", encoding="utf-8") as f:
            json.dump(self.bindings, f, indent=4)

    # -------------------------------------
    # Resolution helpers
    # -------------------------------------
    _MOUSE_BUTTONS: dict[str, int] = {
        "M_LEFT": 0,
        "M_MIDDLE": 1,
        "M_RIGHT": 2,
        "M_X1": 3,
        "M_X2": 4,
    }

    def _binding_base(self, binding_key: str) -> str:
        if not isinstance(binding_key, str):
            return str(binding_key)
        if binding_key.startswith("kb_"):
            body = binding_key[len("kb_") :]
            if body.endswith("_a") or body.endswith("_b"):
                body = body[:-2]
            return body
        if binding_key.startswith("mouse_"):
            return binding_key[len("mouse_") :]
        return binding_key

    @staticmethod
    def _resolve_key_name(name: str) -> Optional[int]:
        """Return pygame key code for a textual key name.
        Accepts K_* names (case-insensitive) and names resolvable by pygame.key.key_code.
        """
        if not name or not isinstance(name, str):
            return None
        up = name.upper()
        if up.startswith("K_"):
            try:
                return getattr(pygame, up)
            except AttributeError:
                # try lowercase variant after K_
                alt = "K_" + name[2:].lower()
                try:
                    return getattr(pygame, alt)
                except AttributeError:
                    return None
        try:
            return pygame.key.key_code(name)
        except Exception:
            return None

    def _default_key_code(self, action: str) -> Optional[int]:
        name = DEFAULT_BINDINGS.get(action)
        if not name:
            return None
        return self._resolve_key_name(name)

    # -------------------------------------
    # Public API: queries
    # -------------------------------------
    def get_key(self, action: str) -> int:
        """
        Return pygame key code for action.
        Preference order: kb_<action>_a -> kb_<action>_b -> user base binding -> default.
        """
        if isinstance(action, str):
            a_code = self.get_key_for_binding(f"kb_{action}_a")
            if a_code is not None:
                return a_code
            b_code = self.get_key_for_binding(f"kb_{action}_b")
            if b_code is not None:
                return b_code
        # user base binding
        name = self.bindings.get(action)
        code = self._resolve_key_name(name) if isinstance(name, str) else None
        if code is not None:
            return code
        # default
        code = self._default_key_code(action)
        if code is not None:
            return code
        raise KeyError(f"No key binding for action '{action}'")

    def get_keys_for_action(self, action: str) -> list[int]:
        """
        Return list of pygame key codes for an action.
        Order: [kb_a, kb_b, base, default], removing duplicates.
        """
        codes: list[int] = []
        if not isinstance(action, str):
            return codes
        # A/B slots
        a_code = self.get_key_for_binding(f"kb_{action}_a")
        b_code = self.get_key_for_binding(f"kb_{action}_b")
        if a_code is not None:
            codes.append(a_code)
        if b_code is not None:
            codes.append(b_code)
        # user base
        name = self.bindings.get(action)
        code = self._resolve_key_name(name) if isinstance(name, str) else None
        if code is not None:
            codes.append(code)
        # default
        code = self._default_key_code(action)
        if code is not None:
            codes.append(code)
        # dedupe
        seen: set[int] = set()
        uniq: list[int] = []
        for c in codes:
            if not isinstance(c, int):
                continue
            if c in seen:
                continue
            seen.add(c)
            uniq.append(c)
        return uniq

    def get_key_for_binding(self, binding_key: str) -> Optional[int]:
        name = self.bindings.get(binding_key, "")
        return self._resolve_key_name(name)

    def get_mouse_button_for_binding(self, binding_key: str) -> Optional[int]:
        name = self.bindings.get(binding_key, "")
        if not name or not isinstance(name, str):
            return None
        return self._MOUSE_BUTTONS.get(name.upper())

    def get_mouse_button(self, action: str) -> int:
        name = self.bindings.get(action)
        if isinstance(name, str) and name.upper().startswith("M_"):
            return self._MOUSE_BUTTONS.get(name.upper(), 0)
        return 0

    # -------------------------------------
    # Public API: mutation
    # -------------------------------------
    def set_key(self, action: str, keyname: str) -> None:
        # Backward compatibility
        self.set_binding(action, keyname)

    def set_binding(self, binding_key: str, value: str, enforce_unique: bool = True) -> None:
        """Set binding value. If enforce_unique, remove same value from other binding keys
        (within same device family K_* or M_*) except the same base action.
        Empty-like values unbind the slot.
        """
        val = (value or "").strip()
        if enforce_unique and val:
            family_prefix: Optional[str] = None
            if isinstance(val, str):
                if val.upper().startswith("K_"):
                    family_prefix = "K_"
                elif val.upper().startswith("M_"):
                    family_prefix = "M_"
            if family_prefix is not None:
                new_base = self._binding_base(binding_key)
                for k, v in list(self.bindings.items()):
                    if k == binding_key:
                        continue
                    if isinstance(v, str) and v.upper() == val.upper():
                        if self._binding_base(k) == new_base:
                            continue
                        self.bindings[k] = ""
        self.bindings[binding_key] = val
        self.save()
