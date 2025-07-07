# Path: src/roguelike_game/config/input_config.py
import pygame
import json
import os

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
                "spell_sphere_magic_shield": "K_h",
                "spell_teleport": "K_j",
                "pause": "K_ESCAPE",
                "toggle_item_editor": "K_F6",
                "drop": "K_d"
            }
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path, 'w', encoding='utf-8') as f:
                json.dump(self.bindings, f, indent=4)
                # Toggle Item Editor binding
        if "toggle_item_editor" not in self.bindings:
            self.bindings["toggle_item_editor"] = "K_F6"
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

    def get_key(self, action):
        # Fallback para Item Editor toggle
        if action == "toggle_item_editor":
            return pygame.K_F6
        if action == "toggle_tile_editor":
            return pygame.K_F8
        if action == "toggle_map_editor":
            return pygame.K_F11
        # Fallback para firework launch si no está en bindings
        if action == "spell_firework_launch":
            return pygame.K_v
        # Fallback para arcane flame si no está en bindings
        if action == "spell_arcane_flame":
            return pygame.K_c
        # Fallback para smoke si no está en bindings
        if action == "spell_smoke":
            return pygame.K_f
        if action == "spell_smoke_emitter":
            return pygame.K_g
        if action == "spell_sphere_magic_shield":
            return pygame.K_h
        if action == "spell_teleport":
            return pygame.K_j
        if action == "drop":
            return pygame.K_d
        keyname = self.bindings.get(action)
        if not keyname:
            raise KeyError(f"No key binding for action '{action}'")
        # Si es constante pygame (K_...)
        if keyname.startswith("K_"):
            try:
                return getattr(pygame, keyname)
            except AttributeError:
                pass
        # Intentar convertir nombre de tecla a código
        try:
            return pygame.key.key_code(keyname)
        except Exception:
            raise ValueError(f"Unknown key name '{keyname}' for action '{action}'")

    def set_key(self, action, keyname):
        self.bindings[action] = keyname

    def save(self):
        with open(self.path, 'w', encoding='utf-8') as f:
            json.dump(self.bindings, f, indent=4)