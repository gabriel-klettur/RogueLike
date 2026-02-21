from __future__ import annotations

import logging
from typing import Any

from roguelike_editors.items.services.items_repository import delete_item as db_delete_item


class ItemsSystemService:
    """Operaciones sobre el sistema de ítems (ficheros JSON) para el Items Editor."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def delete_item_from_system(self, item_id: str) -> bool:
        try:
            ok = db_delete_item(item_id)
            if not ok:
                logging.getLogger(__name__).warning("[ItemsSystemService] delete_item_from_system: '%s' not found", item_id)
                return False
            try:
                self.c._refresh_items_catalog()
            except Exception:
                logging.getLogger(__name__).exception("[ItemsSystemService] Failed to refresh after deleting '%s'", item_id)
            return True
        except Exception:
            logging.getLogger(__name__).exception("[ItemsSystemService] delete_item_from_system failed for '%s'", item_id)
            return False
