import pygame
import json
import os
from typing import Optional

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
                self.bindings = json.load(f)
        else:
            # Valores por defecto
            self.bindings = {
                "move_up": "K_UP",
                "move_down": "K_DOWN",
                "move_left": "K_LEFT",
                "move_right": "K_RIGHT",
                "spell_lightball": "K_q",
                "spell_slash": "K_e",
                "spell_healing_aura": "K_x",
                "spell_darkball": "K_1",
                "spell_iceball": "K_2",
                "spell_arcane_flame": "K_c",
                "spell_firework_launch": "K_v",
                "spell_smoke": "K_f",
                "spell_smoke_emitter": "K_g",
                "spell_sphere_magic_shield": "K_t",
                "spell_teleport": "K_j",
                "pause": "K_ESCAPE",
                # Editor toggles (defaults)
                "toggle_particles_editor": "K_F1",
                "toggle_spawner_editor": "K_F3",
                "toggle_spells_editor": "K_F4",
                "toggle_entities_editor": "K_F5",
                "toggle_inventory_editor": "K_F6",
                "toggle_item_editor": "K_F7",
                "toggle_tile_editor": "K_F8",
                "toggle_building_editor": "K_F10",
                "toggle_map_editor": "K_F11",
                "toggle_fsm_editor": "K_F12",
                # Diagnostics overlay toggle
                "toggle_debug_overlay": "K_F9",
                "select_class": "K_F2",
                # Gameplay inventory (not editor)
                "toggle_inventory": "K_i"
            }
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path, 'w', encoding='utf-8') as f:
                json.dump(self.bindings, f, indent=4)

        # Ensure presence of editor toggle bindings in user config for discoverability
        ensured = False
        if "toggle_particles_editor" not in self.bindings:
            self.bindings["toggle_particles_editor"] = "K_F1"; ensured = True
        if "toggle_spawner_editor" not in self.bindings:
            self.bindings["toggle_spawner_editor"] = "K_F3"; ensured = True
        if "toggle_spells_editor" not in self.bindings:
            self.bindings["toggle_spells_editor"] = "K_F4"; ensured = True
        if "toggle_entities_editor" not in self.bindings:
            self.bindings["toggle_entities_editor"] = "K_F5"; ensured = True
        if "toggle_inventory_editor" not in self.bindings:
            self.bindings["toggle_inventory_editor"] = "K_F6"; ensured = True
        if "toggle_item_editor" not in self.bindings:
            self.bindings["toggle_item_editor"] = "K_F7"; ensured = True
        if "toggle_tile_editor" not in self.bindings:
            self.bindings["toggle_tile_editor"] = "K_F8"; ensured = True
        if "toggle_building_editor" not in self.bindings:
            self.bindings["toggle_building_editor"] = "K_F10"; ensured = True
        if "toggle_map_editor" not in self.bindings:
            self.bindings["toggle_map_editor"] = "K_F11"; ensured = True
        if "toggle_fsm_editor" not in self.bindings:
            self.bindings["toggle_fsm_editor"] = "K_F12"; ensured = True
        # Ensure diagnostics overlay toggle binding exists
        if "toggle_debug_overlay" not in self.bindings:
            self.bindings["toggle_debug_overlay"] = "K_F9"; ensured = True
        if ensured:
            self.save()
        if "toggle_inventory" not in self.bindings:
            self.bindings["toggle_inventory"] = "K_i"
            self.save()
        # Ensure interact binding exists (contextual interactions like vendors, doors)
        if "interact" not in self.bindings:
            self.bindings["interact"] = "K_RETURN"
            self.save()

        # Asegurar binding para lightning
        if "spell_lightning" not in self.bindings:
            self.bindings["spell_lightning"] = "K_r"
        # Asegurar binding para arcane flame
        if "spell_arcane_flame" not in self.bindings:
            if "spell_firework_launch" not in self.bindings:
                self.bindings["spell_firework_launch"] = "K_v"
                self.save()
            self.bindings["spell_arcane_flame"] = "K_c"
            self.save()
            self.bindings["spell_lightning"] = "K_r"

        if "select_class" not in self.bindings:
            self.bindings["select_class"] = "K_F2"
            self.save()

        # Mouse actions defaults (allow configuring dash, fireball, laser beam)
        ensured_mouse = False
        if "mouse_fireball" not in self.bindings:
            self.bindings["mouse_fireball"] = "M_LEFT"  # left click
            ensured_mouse = True
        if "mouse_laser_beam" not in self.bindings:
            self.bindings["mouse_laser_beam"] = "M_MIDDLE"  # middle click
            ensured_mouse = True
        if "mouse_dash" not in self.bindings:
            self.bindings["mouse_dash"] = "M_RIGHT"  # right click
            ensured_mouse = True
        if ensured_mouse:
            self.save()

        # Ensure triple-slot bindings for combat actions (keyboard A/B + mouse)
        ensured_slots = False
        for base in ("fireball", "laser_beam", "dash"):
            kb_a = f"kb_{base}_a"
            kb_b = f"kb_{base}_b"
            mkey = f"mouse_{base}"
            if kb_a not in self.bindings:
                self.bindings[kb_a] = ""  # empty means unbound
                ensured_slots = True
            if kb_b not in self.bindings:
                self.bindings[kb_b] = ""
                ensured_slots = True
            # mouse_{base} ya se asegura arriba, pero por si el archivo del usuario es antiguo
            if mkey not in self.bindings:
                # establecer un valor razonable por defecto
                default_mouse = {
                    "fireball": "M_LEFT",
                    "laser_beam": "M_MIDDLE",
                    "dash": "M_RIGHT",
                }[base]
                self.bindings[mkey] = default_mouse
                ensured_slots = True
        if ensured_slots:
            self.save()

    def get_key(self, action):
        """
        Retorna el código pygame de la tecla para una acción.
        Primero intenta la configuración del usuario, luego aplica valores por defecto.
        """
        # Preferir tri-slot si existe: kb_<action>_a, luego kb_<action>_b
        if isinstance(action, str):
            a_code = self.get_key_for_binding(f"kb_{action}_a")
            if a_code is not None:
                return a_code
            b_code = self.get_key_for_binding(f"kb_{action}_b")
            if b_code is not None:
                return b_code
        # Mapeo de valores por defecto
        defaults = {
            "move_up": pygame.K_UP,
            "move_down": pygame.K_DOWN,
            "move_left": pygame.K_LEFT,
            "move_right": pygame.K_RIGHT,
            "spell_lightball": pygame.K_q,
            "spell_slash": pygame.K_e,
            "spell_healing_aura": pygame.K_x,
            "spell_darkball": pygame.K_1,
            "spell_iceball": pygame.K_2,
            "spell_arcane_flame": pygame.K_c,
            "spell_firework_launch": pygame.K_v,
            "spell_smoke": pygame.K_f,
            "spell_smoke_emitter": pygame.K_g,
            "spell_sphere_magic_shield": pygame.K_t,
            "spell_teleport": pygame.K_j,
            "pause": pygame.K_ESCAPE,
            # Editor toggles (defaults)
            "toggle_particles_editor": pygame.K_F1,
            "toggle_spawner_editor": pygame.K_F3,
            "toggle_spells_editor": pygame.K_F4,
            "toggle_entities_editor": pygame.K_F5,
            "toggle_inventory_editor": pygame.K_F6,
            "toggle_item_editor": pygame.K_F7,
            "toggle_tile_editor": pygame.K_F8,
            "toggle_building_editor": pygame.K_F10,
            "toggle_map_editor": pygame.K_F11,
            "toggle_fsm_editor": pygame.K_F12,
            "toggle_debug_overlay": pygame.K_F9,
            # Gameplay inventory (not editor)
            "toggle_inventory": pygame.K_i,
            "interact": pygame.K_RETURN,
            "select_class": pygame.K_F2
        }

        # Intentar binding de usuario (binding base, p.ej. 'move_up' -> 'K_UP')
        name = self.bindings.get(action)
        if name:
            if name.startswith("K_"):
                try:
                    return getattr(pygame, name)
                except AttributeError:
                    # try lowercase variant after K_
                    alt = "K_" + name[2:].lower()
                    try:
                        return getattr(pygame, alt)
                    except AttributeError:
                        pass
            try:
                return pygame.key.key_code(name)
            except Exception:
                pass
        # Devolver valor por defecto si existe
        if action in defaults:
            return defaults[action]
        # Si no hay binding y no es acción conocida, error
        raise KeyError(f"No key binding for action '{action}'")

    def get_keys_for_action(self, action: str) -> list[int]:
        """Devuelve una lista de keycodes pygame para una acción, agregando:
        - kb_<action>_a y kb_<action>_b si existen
        - binding base (self.bindings[action]) si es una tecla válida
        - valor por defecto si existe en la tabla de defaults

        El orden es [kb_a, kb_b, base, default] y se eliminan duplicados conservando el orden.
        """
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
        name = self.bindings.get(action)
        if isinstance(name, str) and name:
            up = name.upper()
            # Soportar nombres tipo 'K_*' o equivalentes en minúscula
            if up.startswith("K_"):
                try:
                    codes.append(getattr(pygame, up))
                except AttributeError:
                    # probar variante minúscula tras K_
                    alt = "K_" + name[2:].lower()
                    try:
                        codes.append(getattr(pygame, alt))
                    except AttributeError:
                        try:
                            codes.append(pygame.key.key_code(name))
                        except Exception:
                            pass
            else:
                try:
                    codes.append(pygame.key.key_code(name))
                except Exception:
                    pass
        # 3) Valores por defecto (idénticos a los de get_key)
        defaults = {
            "move_up": pygame.K_UP,
            "move_down": pygame.K_DOWN,
            "move_left": pygame.K_LEFT,
            "move_right": pygame.K_RIGHT,
            "spell_lightball": pygame.K_q,
            "spell_slash": pygame.K_e,
            "spell_healing_aura": pygame.K_x,
            "spell_darkball": pygame.K_1,
            "spell_iceball": pygame.K_2,
            "spell_arcane_flame": pygame.K_c,
            "spell_firework_launch": pygame.K_v,
            "spell_smoke": pygame.K_f,
            "spell_smoke_emitter": pygame.K_g,
            "spell_sphere_magic_shield": pygame.K_t,
            "spell_teleport": pygame.K_j,
            "pause": pygame.K_ESCAPE,
            "toggle_particles_editor": pygame.K_F1,
            "toggle_spawner_editor": pygame.K_F3,
            "toggle_spells_editor": pygame.K_F4,
            "toggle_entities_editor": pygame.K_F5,
            "toggle_inventory_editor": pygame.K_F6,
            "toggle_item_editor": pygame.K_F7,
            "toggle_tile_editor": pygame.K_F8,
            "toggle_building_editor": pygame.K_F10,
            "toggle_map_editor": pygame.K_F11,
            "toggle_fsm_editor": pygame.K_F12,
            "toggle_debug_overlay": pygame.K_F9,
            "toggle_inventory": pygame.K_i,
            "interact": pygame.K_RETURN,
            "select_class": pygame.K_F2,
        }
        if action in defaults:
            codes.append(defaults[action])
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
        """Return pygame mouse button index for a mouse action binding.
        Defaults to left click if invalid/not found.
        """
        name = self.bindings.get(action)
        if isinstance(name, str) and name.upper().startswith("M_"):
            return self._MOUSE_BUTTONS.get(name.upper(), 0)
        # fallback
        return 0