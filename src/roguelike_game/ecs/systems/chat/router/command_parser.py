from __future__ import annotations

import re
from typing import Literal, Optional, Tuple

VendorIntent = Literal['stock', 'stock_q', 'gold', 'restock', 'add_wood', 'buy', 'sell']


def parse_vendor_intent(text: str) -> Optional[Tuple[VendorIntent, tuple]]:
    t = text.strip()
    m_stock = re.match(r"^(?:!stock|muestra\s+stock|ver\s+stock|dime\s+stock)(?:\s+(?:de\s+)?(\w+))?$", t, flags=re.IGNORECASE)
    if m_stock:
        return 'stock', ((m_stock.group(1) or 'wood').lower(),)
    m_stock_q = re.match(r"^(?:qu[eé]\s+stock\s+tienes\??|cu[aá]nt[oa]\s+(?:stock|madera|maderas)\s+(?:tienes|ten[ée]s)(?:\s+de\s+(\w+))?\??)$", t, flags=re.IGNORECASE)
    if m_stock_q:
        return 'stock_q', ((m_stock_q.group(1) or 'wood').lower(),)
    m_gold = re.match(r"^(?:!gold|ver\s+oro|muestra\s+oro|cu[aá]nto\s+oro\s+(?:tienes|ten[ée]s)\??)$", t, flags=re.IGNORECASE)
    if m_gold:
        return 'gold', tuple()
    m = re.match(r"^!restock\s+(\d+)\s*(\w+)?$", t, flags=re.IGNORECASE)
    if m:
        return 'restock', (int(m.group(1)), (m.group(2) or 'wood').lower())
    m2 = re.match(r"^(agrega|añade|sumar)\s+(\d+)\s+(madera|wood|wooden)$", t, flags=re.IGNORECASE)
    if m2:
        return 'add_wood', (int(m2.group(2)),)
    m_buy = re.match(r"^(?:buy|comprar|c[oó]mprame|c[oó]mprar)\s+(\d+)\s*(\w+)?$", t, flags=re.IGNORECASE)
    if m_buy:
        return 'buy', (int(m_buy.group(1)), (m_buy.group(2) or 'wood').lower())
    m_sell = re.match(r"^(?:sell|vender|v[eé]ndeme|vende)\s+(\d+)\s*(\w+)?$", t, flags=re.IGNORECASE)
    if m_sell:
        return 'sell', (int(m_sell.group(1)), (m_sell.group(2) or 'wood').lower())
    return None


def is_affirmative(text: str) -> bool:
    return bool(re.match(r"^(?:s[ií]|si|yes|ok|vale|de\s*acuerdo|confirmo|acepto)$", text.strip(), flags=re.IGNORECASE))


def is_negative(text: str) -> bool:
    return bool(re.match(r"^(?:no|cancel[aá]r?|cancelo|mejor\s+no)$", text.strip(), flags=re.IGNORECASE))
