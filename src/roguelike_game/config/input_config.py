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
                "spell_sphere_magic_shield": "K_t",
                "spell_teleport": "K_j",
                "pause": "K_ESCAPE",
                # Editor toggles (defaults)
                "toggle_spawner_editor": "K_F3",
                "toggle_spells_editor": "K_F4",
                "toggle_entities_editor": "K_F5",
                "toggle_inventory_editor": "K_F6",
                "toggle_item_editor": "K_F7",
                "toggle_tile_editor": "K_F8",
                "toggle_building_editor": "K_F10",
                "toggle_map_editor": "K_F11",
                "toggle_fsm_editor": "K_F12",
                "select_class": "K_F2",
                # Gameplay inventory (not editor)
                "toggle_inventory": "K_i"
            }
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path, 'w', encoding='utf-8') as f:
                json.dump(self.bindings, f, indent=4)

        # Ensure presence of editor toggle bindings in user config for discoverability
        ensured = False
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
        if ensured:
            self.save()
        if "toggle_inventory" not in self.bindings:
            self.bindings["toggle_inventory"] = "K_i"
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

    def get_key(self, action):
        """
        Retorna el código pygame de la tecla para una acción.
        Primero intenta la configuración del usuario, luego aplica valores por defecto.
        """
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
            "toggle_spawner_editor": pygame.K_F3,
            "toggle_spells_editor": pygame.K_F4,
            "toggle_entities_editor": pygame.K_F5,
            "toggle_inventory_editor": pygame.K_F6,
            "toggle_item_editor": pygame.K_F7,
            "toggle_tile_editor": pygame.K_F8,
            "toggle_building_editor": pygame.K_F10,
            "toggle_map_editor": pygame.K_F11,
            "toggle_fsm_editor": pygame.K_F12,
            # Gameplay inventory (not editor)
            "toggle_inventory": pygame.K_i,
            "select_class": pygame.K_F2
        }

        # Intentar binding de usuario
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

    def set_key(self, action, keyname):
        self.bindings[action] = keyname

    def save(self):
        with open(self.path, 'w', encoding='utf-8') as f:
            json.dump(self.bindings, f, indent=4)