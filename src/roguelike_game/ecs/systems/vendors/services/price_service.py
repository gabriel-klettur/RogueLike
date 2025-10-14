from __future__ import annotations

import json
import logging
import os
from typing import Any, Dict, Optional

import jsonschema

logger = logging.getLogger(__name__)


class PriceService:
    """Load and provide item prices with schema validation and catalog fallback.

    - Global prices file: data/items/items_price.json
      Accepts number or object {"buy": n, "sell": n}.
    - Catalog file (fallback): data/items/items.json
      If no price, derive from catalog: value if present; else 1.0 if stackable else 10.0
      Currency id 'gold' has no derived fallback.
    - Optional schema: schemas/items/ItemsPriceSchema.json
    """

    def __init__(self,
                 prices_path: str | None = None,
                 items_catalog_path: str | None = None,
                 prices_schema_path: str | None = None) -> None:
        self._prices_path = prices_path or os.path.join('data', 'items', 'items_price.json')
        self._items_catalog_path = items_catalog_path or os.path.join('data', 'items', 'items.json')
        self._prices_schema_path = prices_schema_path or os.path.join('schemas', 'items', 'ItemsPriceSchema.json')

        self._global_prices: Dict[str, Dict[str, float]] | None = None
        self._global_prices_mtime: float | None = None

        self._items_catalog: Dict[str, Dict[str, Any]] | None = None
        self._items_catalog_mtime: float | None = None

        self._prices_schema: Dict[str, Any] | None = None

    # ---------------------- Public API ----------------------
    def get_global_price(self, item_id: str, side: str) -> Optional[float]:
        """Return price for side ('buy'|'sell') from global prices or fallback.
        Returns None if not available.
        """
        try:
            self._ensure_prices_loaded()
            if isinstance(self._global_prices, dict):
                entry = self._global_prices.get(item_id)
                if isinstance(entry, dict):
                    v = entry.get(side)
                    return float(v) if self._is_number(v) else None
            fb = self._fallback_price_from_catalog(item_id)
            if fb is not None:
                return float(fb)
        except Exception:
            logger.exception("get_global_price failed")
        return None

    def ensure_items_catalog_loaded(self) -> None:
        self._ensure_items_catalog_loaded()

    def get_items_catalog(self) -> Dict[str, Dict[str, Any]]:
        return self._items_catalog or {}

    # ---------------------- Internal ------------------------
    def _ensure_prices_loaded(self) -> None:
        path = self._prices_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._global_prices = {}
            self._global_prices_mtime = None
            return
        if self._global_prices is None or self._global_prices_mtime != mtime:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                self._ensure_prices_schema_loaded()
                try:
                    if self._prices_schema is not None:
                        jsonschema.validate(instance=data, schema=self._prices_schema)
                except Exception:
                    data = {}
                parsed: Dict[str, Dict[str, float]] = {}
                if isinstance(data, dict):
                    for k, v in data.items():
                        key = str(k)
                        if self._is_number(v):
                            fv = float(v)
                            parsed[key] = {'buy': fv, 'sell': fv}
                        elif isinstance(v, dict):
                            buy_v = v.get('buy', v.get('price', None))
                            sell_v = v.get('sell', v.get('price', None))
                            entry: Dict[str, float] = {}
                            if self._is_number(buy_v):
                                entry['buy'] = float(buy_v)
                            if self._is_number(sell_v):
                                entry['sell'] = float(sell_v)
                            if entry:
                                if 'buy' not in entry and 'sell' in entry:
                                    entry['buy'] = entry['sell']
                                if 'sell' not in entry and 'buy' in entry:
                                    entry['sell'] = entry['buy']
                                parsed[key] = entry
                self._global_prices = parsed
                self._global_prices_mtime = mtime
            except Exception:
                logger.exception("Failed to load prices file")
                self._global_prices = {}
                self._global_prices_mtime = mtime

    def _ensure_items_catalog_loaded(self) -> None:
        path = self._items_catalog_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._items_catalog = {}
            self._items_catalog_mtime = None
            return
        if self._items_catalog is None or self._items_catalog_mtime != mtime:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                self._items_catalog = data if isinstance(data, dict) else {}
            except Exception:
                logger.exception("Failed to load items catalog")
                self._items_catalog = {}
            self._items_catalog_mtime = mtime

    def _fallback_price_from_catalog(self, item_id: str) -> Optional[float]:
        try:
            self._ensure_items_catalog_loaded()
            cat = self._items_catalog or {}
            node = cat.get(item_id)
            if not isinstance(node, dict):
                return None
            if item_id == 'gold':  # avoid setting price for currency
                return None
            if 'value' in node and self._is_number(node.get('value')):
                return float(node.get('value'))
            stackable = bool(node.get('stackable', False))
            return 1.0 if stackable else 10.0
        except Exception:
            logger.exception("fallback price from catalog failed")
            return None

    def _ensure_prices_schema_loaded(self) -> None:
        if self._prices_schema is not None:
            return
        try:
            with open(self._prices_schema_path, 'r', encoding='utf-8') as f:
                self._prices_schema = json.load(f)
        except Exception:
            self._prices_schema = None

    @staticmethod
    def _is_number(x: Any) -> bool:
        try:
            float(x)
            return True
        except Exception:
            return False
