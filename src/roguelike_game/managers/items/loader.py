"""
Loader de ítems (data only): carga catálogo de ítems y sus assets.
"""
from pathlib import Path
from typing import Dict, Any
import json

from roguelike_engine.utils.loader import load_image
from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow
from roguelike_game.ecs.components.item_models import (
    ItemModel,
    ConsumableItemModel,
    EquipableItemModel,
    QuestItemModel,
)

import logging
logger = logging.getLogger(__name__)


class ItemsLoader:
    """
    Carga ítems desde SQLite junto con sus assets de iconos.
    """

    def _select_model_cls(self, payload: Dict[str, Any]):
        if payload.get("effect") is not None:
            return ConsumableItemModel
        if payload.get("equip_slot") is not None or payload.get("durability") is not None:
            return EquipableItemModel
        if payload.get("quest_id") is not None:
            return QuestItemModel
        return ItemModel

    def load(self):
        items: Dict[str, ItemModel] = {}
        assets = {}

        with session_scope() as s:
            rows = s.query(ItemRow).all()
            for row in rows:
                payload: Dict[str, Any] = {}
                try:
                    if row.extra_json:
                        payload = json.loads(row.extra_json)
                except Exception:
                    payload = {}

                # Overlay stable columns from DB over payload
                payload.update({k: v for k, v in {
                    "id": row.id,
                    "name": row.name,
                    "description": row.description,
                    "stackable": row.stackable,
                    "max_stack": row.max_stack,
                    "z_layer": row.z_layer,
                    "despawn_time": row.despawn_time,
                    "equip_slot": row.equip_slot,
                    "rarity": row.rarity,
                    "level_requirement": row.level_requirement,
                    "icon_small": row.icon_small,
                    "icon_large": row.icon_large,
                }.items() if v is not None})

                # Handle icon list stored in icon_json
                if getattr(row, "icon_json", None):
                    try:
                        payload["icon"] = json.loads(row.icon_json)
                    except Exception:
                        pass

                model_cls = self._select_model_cls(payload)
                try:
                    model = model_cls(**payload)
                except Exception:
                    # Fallback to base model if subclass validation fails
                    model = ItemModel(**{k: v for k, v in payload.items() if k in ItemModel.model_fields})

                items[row.id] = model

                # Load one icon asset per item if available
                icon_paths = []
                if getattr(model, "icon", None):
                    icon_paths = model.icon if isinstance(model.icon, list) else [model.icon]
                else:
                    if model.icon_small:
                        icon_paths.append(model.icon_small)
                    if model.icon_large:
                        icon_paths.append(model.icon_large)
                if icon_paths:
                    try:
                        assets[row.id] = load_image(icon_paths[0])
                    except Exception as e:
                        logger.error(f"Error cargando icono {row.id}: {e}")

        return items, assets
