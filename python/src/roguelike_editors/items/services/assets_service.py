from __future__ import annotations

import logging
from typing import Any, Optional

import pygame

from roguelike_engine.utils.loader import load_image


class ItemsAssetsService:
    """Gestión de anclas de assets y actualización de imágenes en el Items Editor."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def get_assets_anchor_rect(self) -> Optional[pygame.Rect]:
        try:
            rect = getattr(self.c.picker_controller, 'picker_state', None)
            if rect is not None and getattr(rect, 'rect', None):
                return rect.rect
        except Exception:
            pass
        return None

    def on_asset_changed(self, item_id: str, new_asset_path: str) -> None:
        try:
            img = load_image(new_asset_path)
            self.c.model.assets[item_id] = img
            try:
                self.c.picker_controller.model.assets[item_id] = img
            except Exception:
                pass
            try:
                self.c.picker_controller.view.assets[item_id] = img
            except Exception:
                pass
        except Exception:
            logging.getLogger(__name__).exception("[ItemsAssetsService] Failed to refresh asset image for '%s'", item_id)
