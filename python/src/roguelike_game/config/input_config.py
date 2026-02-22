import pygame
import json
import os
from typing import Optional

from ._input_defaults import DEFAULT_BINDINGS, MOUSE_DEFAULTS, TRISLOT_BASES

class InputConfig:
    # Singleton por ruta de config
    _instances: dict[str, 'InputConfig'] = {}
    def __new__(cls, path=None):
        # Determinar ruta absoluta de config
        resolved = path or os.path.join(os.getcwd(), 'data', 'config', 'input_bindings.json')
        if resolved in cls._instances:
            return cls._instances[resolved]
        inst = super().__new__(cls)
        cls._instances[resolved] = inst
        return inst
    
    def __init__(self, path=None):
        # Evitar re-inicializar si ya cargado
        if getattr(self, '_initialized', False):
            return
        self._initialized = True
        # Ruta por defecto al JSON de bindings
        self.path = path or os.path.join(os.getcwd(), 'data', 'config', 'input_bindings.json')
        self.bindings = {}
        self._load()

    def _load(self):
        if os.path.exists(self.path):
            with open(self.path, 'r', encoding='utf-8') as f:
                try:
                    self.bindings = json.load(f) or {}
                except Exception:
                    self.bindings = {}
        else:
            self.bindings = {}

        ensured = False
        # Ensure keyboard defaults
        for k, v in DEFAULT_BINDINGS.items():
            if k not in self.bindings:
                self.bindings[k] = v
                ensured = True
        # Ensure mouse defaults
        for k, v in MOUSE_DEFAULTS.items():
            if k not in self.bindings:
                self.bindings[k] = v
                ensured = True
        # Ensure tri-slot A/B and mouse_{base}
        for base in TRISLOT_BASES:
            a = f"kb_{base}_a"
            b = f"kb_{base}_b"
            if a not in self.bindings:
                self.bindings[a] = ""
                ensured = True
            if b not in self.bindings:
                self.bindings[b] = ""
                ensured = True
            mkey = f"mouse_{base}"
            if mkey not in self.bindings:
                default_mouse = MOUSE_DEFAULTS.get(mkey)
                if default_mouse:
                    self.bindings[mkey] = default_mouse
                    ensured = True
        # Migration: old reload_data K_F5 -> K_F1
        try:
            if str(self.bindings.get("reload_data", "")).upper() == "K_F5":
                self.bindings["reload_data"] = "K_F1"
                ensured = True
        except Exception:
            pass
        if ensured:
            self.save()

    def get_key(self, action):
        """Retorna el keycode pygame para una acción (preferencia: slots A/B, usuario, default)."""
        # Preferir tri-slot si existe: kb_<action>_a, luego kb_<action>_b
        if isinstance(action, str):
            a_code = self.get_key_for_binding(f"kb_{action}_a")
            if a_code is not None:
                return a_code
            b_code = self.get_key_for_binding(f"kb_{action}_b")
            if b_code is not None:
                return b_code
        # Intentar binding de usuario y luego default
        code = self._resolve_key_name(self.bindings.get(action))
        if code is not None:
            return code
        code = self._resolve_key_name(DEFAULT_BINDINGS.get(action))
        if code is not None:
            return code
        # Si no hay binding y no es acción conocida, error
        raise KeyError(f"No key binding for action '{action}'")

    def get_keys_for_action(self, action: str) -> list[int]:
        """Devuelve keycodes pygame para una acción: [kb_a, kb_b, base, default] sin duplicados."""
        codes: list[int] = []
        if not isinstance(action, str):
            return codes
        # 1) Slots A/B
        a_code = self.get_key_for_binding(f"kb_{action}_a")
        b_code = self.get_key_for_binding(f"kb_{action}_b")
        if a_code is not None:
            codes.append(a_code)
        if b_code is not None:
            codes.append(b_code)
        # 2) Binding base de usuario (si es tecla)
        base_code = self._resolve_key_name(self.bindings.get(action))
        if isinstance(base_code, int):
            codes.append(base_code)
        # 3) Valor por defecto (idéntico a get_key, vía DEFAULT_BINDINGS)
        default_code = self._resolve_key_name(DEFAULT_BINDINGS.get(action))
        if isinstance(default_code, int):
            codes.append(default_code)
        # 4) Deduplicar preservando orden
        seen = set()
        uniq: list[int] = []
        for c in codes:
            if not isinstance(c, int):
                continue
            if c in seen:
                continue
            seen.add(c)
            uniq.append(c)
        return uniq

    def set_key(self, action, keyname):
        # Maintain backward compatibility while enforcing global uniqueness
        self.set_binding(action, keyname)

    # --- New slot-based helpers with uniqueness enforcement ---
    def set_binding(self, binding_key: str, value: str, enforce_unique: bool = True):
        """Set a binding value (e.g., 'kb_fireball_a' -> 'K_Z' or 'mouse_fireball' -> 'M_RIGHT').
        If enforce_unique is True, remove this same value from any other binding keys to guarantee global uniqueness.
        Empty/false-y values unbind the slot.
        """
        val = (value or "").strip()
        # Enforce uniqueness across all bindings of the same family (K_* among keys, M_* among mouse)
        if enforce_unique and val:
            family_prefix = None
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
                        # Do not clear if both belong to the same action base (allow base+slot duplicates)
                        if self._binding_base(k) == new_base:
                            continue
                        # Clear previous owner to keep uniqueness across different actions
                        self.bindings[k] = ""
        # Finally set
        self.bindings[binding_key] = val
        self.save()

    def _binding_base(self, binding_key: str) -> str:
        """Return the logical base action for a binding key.
        Examples:
        - 'kb_fireball_a' -> 'fireball'
        - 'kb_fireball_b' -> 'fireball'
        - 'mouse_fireball' -> 'fireball'
        - 'move_up' -> 'move_up'
        - 'spell_lightball' -> 'spell_lightball'
        """
        if not isinstance(binding_key, str):
            return str(binding_key)
        if binding_key.startswith('kb_'):
            body = binding_key[len('kb_'):]
            if body.endswith('_a') or body.endswith('_b'):
                body = body[:-2]
            return body
        if binding_key.startswith('mouse_'):
            return binding_key[len('mouse_'):]
        return binding_key

    def _resolve_key_name(self, name: Optional[str]) -> Optional[int]:
        """Resuelve un nombre textual a keycode pygame. Acepta K_* y nombres key_code."""
        if not name or not isinstance(name, str):
            return None
        up = name.upper()
        if up.startswith("K_"):
            try:
                return getattr(pygame, up)
            except AttributeError:
                alt = "K_" + name[2:].lower()
                try:
                    return getattr(pygame, alt)
                except AttributeError:
                    return None
        try:
            return pygame.key.key_code(name)
        except Exception:
            return None

    def get_key_for_binding(self, binding_key: str) -> Optional[int]:
        """Resolve pygame keycode for a K_* binding entry; returns None if unbound or non-key."""
        name = self.bindings.get(binding_key, "")
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
        return None

    def get_mouse_button_for_binding(self, binding_key: str) -> Optional[int]:
        """Resolve mouse button index for an M_* binding entry; returns None if unbound or non-mouse."""
        name = self.bindings.get(binding_key, "")
        if not name or not isinstance(name, str):
            return None
        up = name.upper()
        if up.startswith("M_"):
            return self._MOUSE_BUTTONS.get(up)
        return None

    def save(self):
        with open(self.path, 'w', encoding='utf-8') as f:
            json.dump(self.bindings, f, indent=4)

    # --- Mouse helpers ---
    _MOUSE_BUTTONS = {
        "M_LEFT": 0,
        "M_MIDDLE": 1,
        "M_RIGHT": 2,
        "M_X1": 3,
        "M_X2": 4,
    }

    def get_mouse_button(self, action: str) -> int:
        """Devuelve el índice de botón de ratón; por defecto, izquierdo (0)."""
        name = self.bindings.get(action)
        if isinstance(name, str) and name.upper().startswith("M_"):
            return self._MOUSE_BUTTONS.get(name.upper(), 0)
        # fallback
        return 0