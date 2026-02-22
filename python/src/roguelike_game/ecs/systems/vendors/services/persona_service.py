from __future__ import annotations

import json
import logging
import os
from typing import Any, Dict, Optional

logger = logging.getLogger(__name__)


class PersonaService:
    """Apply persona-based negotiation caps (discount limits).

    Reads:
      - data/chat/assignments.json maps entity name/id -> {"persona_id": str}
      - data/chat/personas/{persona_id}.json with { negotiation: { discount_limits: { item_id | "default": number } } }
    """

    def resolve_persona_id(self, world, vendor_eid: int) -> Optional[str]:
        try:
            comps = world.components.get('Identity', {})
            ident = comps.get(vendor_eid)
            ent_key = getattr(ident, 'name', None) or getattr(ident, 'id', None)
            if not ent_key:
                return None
            ap = os.path.join('data', 'chat', 'assignments.json')
            with open(ap, 'r', encoding='utf-8') as f:
                data = json.load(f)
            node = data.get(str(ent_key)) or data.get(ent_key)
            if isinstance(node, dict):
                return node.get('persona_id')
        except Exception:
            logger.exception("resolve_persona_id failed")
            return None
        return None

    def apply_negotiation(self, world, vendor_eid: int, item_id: str, price: Optional[float], side: str) -> Optional[float]:
        if price is None:
            return None
        try:
            pid = self.resolve_persona_id(world, vendor_eid)
            if not pid:
                return price
            ppath = os.path.join('data', 'chat', 'personas', f'{pid}.json')
            with open(ppath, 'r', encoding='utf-8') as f:
                pobj = json.load(f)
            nego = (pobj.get('negotiation') or {})
            limits = (nego.get('discount_limits') or {})
            lim = limits.get(item_id)
            if lim is None:
                lim = limits.get('default', 0)
            try:
                lim = float(lim)
            except Exception:
                lim = 0.0
            lim = max(0.0, min(0.9, lim))
            if side == 'buy' and lim > 0:
                return max(0.01, float(price) * (1.0 - lim))
            return float(price)
        except Exception:
            logger.exception("apply_negotiation failed")
            return price
