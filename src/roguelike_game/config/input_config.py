import pygame
import json
import os

class InputConfig:
    def __init__(self, path=None):
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
                "attack": "K_SPACE",
                "skill_q": "K_q",
                "skill_e": "K_e",
                "skill_x": "K_x",
                "pause": "K_ESCAPE"
            }
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path, 'w', encoding='utf-8') as f:
                json.dump(self.bindings, f, indent=4)

    def get_key(self, action):
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
