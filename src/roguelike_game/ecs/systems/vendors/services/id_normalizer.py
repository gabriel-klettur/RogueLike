from __future__ import annotations

import logging
from typing import Dict, Optional

from .price_service import PriceService

logger = logging.getLogger(__name__)


class IdNormalizer:
    """Normalize item id and resolve currency id using vendor component and catalog names."""

    def __init__(self, price_service: PriceService) -> None:
        self._price_service = price_service

    def normalize_ids(self, world, vendor_eid: int, item_id: str) -> tuple[str, str]:
        comps = world.components.get('VendorComponent', {})
        vc = comps.get(vendor_eid)
        currency = getattr(vc, 'currency_item_id', 'gold') if vc else 'gold'
        iid = (item_id or '').lower()
        if iid in ('wooden', 'madera'):
            iid = 'wood'
        if str(currency).lower() in ('oro',):
            currency = 'gold'
        if iid not in {'wood', 'gold'}:
            try:
                self._price_service.ensure_items_catalog_loaded()
                cat: Dict[str, Dict] = self._price_service.get_items_catalog()
                if iid not in cat:
                    name_to_id: Dict[str, str] = {}
                    for k, node in cat.items():
                        if isinstance(node, dict):
                            nm = str(node.get('name', '')).strip().lower()
                            if nm:
                                name_to_id[nm] = k
                    mapped = name_to_id.get(iid)
                    if mapped:
                        iid = mapped
            except Exception:
                logger.exception("normalize_ids failed")
        return iid, currency
