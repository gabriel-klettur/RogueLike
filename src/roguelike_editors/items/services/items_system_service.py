from __future__ import annotations

import json
import logging
import os
from typing import Any

from roguelike_ui.services.json_persistence import load_from_json


class ItemsSystemService:
    """Operaciones sobre el sistema de ítems (ficheros JSON) para el Items Editor."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def delete_item_from_system(self, item_id: str) -> bool:
        try:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
            data = load_from_json(items_path)
            if item_id not in data:
                logging.getLogger(__name__).warning("[ItemsSystemService] delete_item_from_system: '%s' not found", item_id)
                return False
            del data[item_id]
            with open(items_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            try:
                self.c._refresh_items_catalog()
            except Exception:
                logging.getLogger(__name__).exception("[ItemsSystemService] Failed to refresh after deleting '%s'", item_id)
            return True
        except Exception:
            logging.getLogger(__name__).exception("[ItemsSystemService] delete_item_from_system failed for '%s'", item_id)
            return False
