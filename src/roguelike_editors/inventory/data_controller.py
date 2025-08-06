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
        logger.debug("[DEBUG][Controller] DataController.load_data start")
        for cat, p in self.paths.items():
            default_data = load_from_json(p['default'])
            self.model.default_data[cat] = default_data
            active = load_from_json(p['active'])
            if cat == 'map' and isinstance(active, dict) and 'map' in active:
                active = active['map']
                os.makedirs(os.path.dirname(p['active']), exist_ok=True)
                with open(p['active'], 'w', encoding='utf-8') as f:
                    json.dump(active, f, ensure_ascii=False, indent=2)
            self.model.active_data[cat] = active
        logger.debug(f"[DEBUG][Controller] DataController.load_data complete. Loaded categories: {list(self.model.default_data.keys())}")
        # Validate JSON schemas if available
        logger.debug(f"[DEBUG][Controller] Loading inventory data for category '{cat}'")
        default_data = load_from_json(p['default'])
        self.model.default_data[cat] = default_data
        logger.debug(f"[DEBUG][Controller] Loaded default_data['{cat}']: {default_data}")
        active = load_from_json(p['active'])
        # Handle nested map data
        if cat == 'map' and isinstance(active, dict) and 'map' in active:
            active = active['map']
            os.makedirs(os.path.dirname(p['active']), exist_ok=True)
            with open(p['active'], 'w', encoding='utf-8') as f:
                json.dump(active, f, ensure_ascii=False, indent=2)
        self.model.active_data[cat] = active
        logger.debug(f"[DEBUG][Controller] Loaded active_data['{cat}']: {active}")
        logger.debug(f"[DEBUG][Controller] DataController.load_data complete. Categories loaded: {list(self.model.default_data.keys())}")
        # Validate JSON schemas if available
        logger.debug("[DEBUG][Controller] DataController.load_data start")
        # Load JSON data into model
        for cat, p in self.paths.items():
            logger.debug(f"[DEBUG][Controller] Loading inventory data for category '{cat}'")
            default_data = load_from_json(p['default'])
            self.model.default_data[cat] = default_data
            logger.debug(f"[DEBUG][Controller] Loaded default_data['{cat}']: {default_data}")
            active = load_from_json(p['active'])
            # Handle nested map data
            if cat == 'map' and isinstance(active, dict) and 'map' in active:
                active = active['map']
                os.makedirs(os.path.dirname(p['active']), exist_ok=True)
                with open(p['active'], 'w', encoding='utf-8') as f:
                    json.dump(active, f, ensure_ascii=False, indent=2)
                    self.model.active_data[cat] = active
            logger.debug(f"[DEBUG][Controller] Loaded active_data['{cat}']: {active}")

        logger.debug(f"[DEBUG][Controller] DataController.load_data complete. Categories loaded: {list(self.model.default_data.keys())}")
        # Validate JSON schemas if available
        try:
            import jsonschema
            schemas_dir = os.path.join(PROJECT_ROOT, 'schemas', 'inventory')
            schemas = {}
            for cat_name, fname in [('player', 'InventoryPlayerSchema.json'),
                                     ('monsters', 'InventoryMonstersSchema.json'),
                                     ('map', 'InventoryMapSchema.json')]:
                path = os.path.join(schemas_dir, fname)
                with open(path, encoding='utf-8') as sf:
                    schemas[cat_name] = json.load(sf)
            # Validate default_data
            for c, data in self.model.default_data.items():
                try:
                    jsonschema.validate(data, schemas.get(c, {}))
                except Exception as ve:
                    self.logger.warning(f"Default data for '{c}' invalid: {ve}")
            # Validate active_data entries
            for c, entries in self.model.active_data.items():
                schema = schemas.get(c, {})
                if c == 'map' and isinstance(entries, dict):
                    try:
                        jsonschema.validate(entries, schema)
                    except Exception as ve:
                        self.logger.warning(f'Active data for "{c}" invalid: {ve}')
                elif isinstance(entries, dict):
                    for key, entry in entries.items():
                        try:
                            jsonschema.validate(entry, schema)
                        except Exception as ve:
                            self.logger.warning(f'Active entry "{key}" for "{c}" invalid: {ve}')
        except ImportError:
            self.logger.warning("jsonschema not installed; skipping schema validation")
