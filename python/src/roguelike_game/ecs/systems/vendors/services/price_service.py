from __future__ import annotations

import logging
from typing import Any, Dict, Optional

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow, ItemPrice as ItemPriceRow

logger = logging.getLogger(__name__)


class PriceService:
    """Provide item prices from SQLite with catalog-based fallback.

    - Primary source: table `item_prices` (columns: id_item, buy_price, sell_price).
    - Fallback: table `items` using `extra_json` value or stackable heuristic.
    - Keeps public API compatible with previous JSON-backed version.
    """

    def __init__(self, *_args, **_kwargs) -> None:  # keep signature flexible for compatibility
        pass

    # ---------------------- Public API ----------------------
    def get_global_price(self, item_id: str, side: str) -> Optional[float]:
        """Return price for side ('buy'|'sell') from DB or fallback; None if unavailable."""
        try:
            side_l = (side or '').lower()
            if side_l not in ('buy', 'sell'):
                side_l = 'buy'
            v = self._get_price_from_db(item_id, side_l)
            if v is not None:
                return float(v)
            fb = self._fallback_price_from_db_catalog(item_id)
            if fb is not None:
                return float(fb)
        except Exception:
            logger.exception("get_global_price failed (db)")
        return None

    # Backwards-compat no-ops
    def ensure_items_catalog_loaded(self) -> None:
        return None

    def get_items_catalog(self) -> Dict[str, Dict[str, Any]]:
        return {}

    # ---------------------- Internal (DB) -------------------
    def _get_price_from_db(self, item_id: str, side: str) -> Optional[float]:
        try:
            with session_scope() as s:
                row = s.get(ItemPriceRow, item_id)
                if row is None:
                    return None
                if side == 'buy':
                    return float(getattr(row, 'buy_price', 0) or 0)
                return float(getattr(row, 'sell_price', 0) or 0)
        except Exception:
            logger.exception("_get_price_from_db failed")
            return None

    def _fallback_price_from_db_catalog(self, item_id: str) -> Optional[float]:
        try:
            if item_id == 'gold':
                return None
            with session_scope() as s:
                it = s.get(ItemRow, item_id)
                if it is None:
                    return None
                # Heurística basada solo en columnas normalizadas
                stackable = bool(getattr(it, 'stackable', False) or False)
                return 1.0 if stackable else 10.0
        except Exception:
            logger.exception("_fallback_price_from_db_catalog failed")
            return None

    @staticmethod
    def _is_number(x: Any) -> bool:
        try:
            float(x)
            return True
        except Exception:
            return False
