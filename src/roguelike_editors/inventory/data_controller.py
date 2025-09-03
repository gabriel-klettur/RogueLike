import os
import json
import logging
from roguelike_engine.config.config import DATA_DIR, PROJECT_ROOT
from roguelike_ui.services.json_persistence import load_from_json
import logging
logger = logging.getLogger(__name__)

class DataController:
    """
    Controller for loading and validating inventory JSON data.
    """
    def __init__(self, model):
        self.model = model
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.paths = {
            'player': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_player.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_player.json'),
            },
            'monsters': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_monsters.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_monsters.json'),
            },
            'map': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_map.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_map.json'),
            },
        }

    def load_data(self):
        logger.debug(" DataController.load_data start")
        # Load JSON data into model
        for cat, p in self.paths.items():
            logger.debug(f" Loading inventory data for category '{cat}'")
            default_data = load_from_json(p['default'])
            self.model.default_data[cat] = default_data
            logger.debug(f" Loaded default_data['{cat}']: {default_data}")
            active = load_from_json(p['active'])
            # Handle nested map data
            if cat == 'map' and isinstance(active, dict) and 'map' in active:
                active = active['map']
                os.makedirs(os.path.dirname(p['active']), exist_ok=True)
                with open(p['active'], 'w', encoding='utf-8') as f:
                    json.dump(active, f, ensure_ascii=False, indent=2)
            self.model.active_data[cat] = active
            logger.debug(f" Loaded active_data['{cat}']: {active}")

        # Alias: exponer 'hostile' como sinónimo de 'monsters' para transición
        try:
            if 'monsters' in self.model.default_data:
                self.model.default_data['hostile'] = self.model.default_data['monsters']
            if 'monsters' in self.model.active_data:
                self.model.active_data['hostile'] = self.model.active_data['monsters']
        except Exception:
            pass

        # Ensure player defaults support per-class templates (non-destructive migration in-memory)
        try:
            self._ensure_player_classes()
        except Exception as e:
            self.logger.warning(f"ensure_player_classes failed: {e}")

        logger.debug(f" DataController.load_data complete. Categories loaded: {list(self.model.default_data.keys())}")
        # Validate JSON schemas if available
        try:
            import jsonschema
            schemas_dir = os.path.join(PROJECT_ROOT, 'schemas', 'inventory')
            schemas = {}
            for cat_name, fname in [
                ('player', 'InventoryPlayerSchema.json'),
                ('monsters', 'InventoryMonstersSchema.json'),
                ('monsters_active', 'InventoryMonstersActiveSchema.json'),
                ('map', 'InventoryMapSchema.json')
            ]:
                path = os.path.join(schemas_dir, fname)
                with open(path, encoding='utf-8') as sf:
                    schemas[cat_name] = json.load(sf)
            # Validate default_data
            for c, data in self.model.default_data.items():
                try:
                    schema_key = 'monsters' if c == 'hostile' else c
                    jsonschema.validate(data, schemas.get(schema_key, {}))
                except Exception as ve:
                    self.logger.warning(f"Default data for '{c}' invalid: {ve}")
            # Validate active_data entries
            for c, entries in self.model.active_data.items():
                schema_key = 'monsters' if c == 'hostile' else c
                schema = schemas.get(schema_key, {})
                if c == 'map' and isinstance(entries, dict):
                    try:
                        jsonschema.validate(entries, schema)
                    except Exception as ve:
                        self.logger.warning(f'Active data for "{c}" invalid: {ve}')
                elif isinstance(entries, dict):
                    for key, entry in entries.items():
                        try:
                            entry_schema = schemas.get('monsters_active', {}) if c in ('monsters', 'hostile') else schema
                            jsonschema.validate(entry, entry_schema)
                        except Exception as ve:
                            self.logger.warning(f'Active entry "{key}" for "{c}" invalid: {ve}')
        except ImportError:
            self.logger.warning("jsonschema not installed; skipping schema validation")

    def _ensure_player_classes(self):
        """
        If default player inventory is in legacy format, expand to a classes map using
        the available class names from data/entities/new_players.json. Copies the same
        capacity and slots to each class. Does not write to disk.
        """
        player_defaults = self.model.default_data.get('player', {}) or {}
        if not isinstance(player_defaults, dict):
            return
        if 'classes' in player_defaults and isinstance(player_defaults['classes'], dict) and player_defaults['classes']:
            return  # already migrated
        # Only migrate when legacy keys are present; otherwise respect loaded structure
        if 'capacity' not in player_defaults and 'slots' not in player_defaults:
            return
        # legacy structure expected keys
        capacity = player_defaults.get('capacity', 0)
        slots = player_defaults.get('slots', []) or []
        # Load class names from new_players.json
        try:
            players_path = os.path.join(DATA_DIR, 'entities', 'new_players.json')
            with open(players_path, 'r', encoding='utf-8') as pf:
                pdata = json.load(pf)
            cls_map = (((pdata or {}).get('players') or {}).get('classes') or {})
            class_names = list(cls_map.keys())
        except Exception as e:
            self.logger.warning(f"Unable to read player classes from new_players.json: {e}")
            class_names = []
        if not class_names:
            # Fallback to a single default class
            class_names = ['default']
        classes = {}
        for name in class_names:
            # Deep copy slots to avoid shared list mutation
            slots_copy = []
            for s in slots:
                if s is None:
                    slots_copy.append(None)
                elif isinstance(s, dict):
                    slots_copy.append(dict(s))
            classes[name] = {
                'capacity': capacity,
                'slots': slots_copy,
            }
        self.model.default_data['player'] = {
            'classes': classes,
            'schema_version': str((self.model.default_data.get('player') or {}).get('schema_version', '1.1.0'))
        }
