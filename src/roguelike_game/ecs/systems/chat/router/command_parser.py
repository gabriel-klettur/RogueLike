from __future__ import annotations

import re
from typing import Literal, Optional, Tuple

VendorIntent = Literal['stock', 'stock_q', 'stock_list', 'gold', 'restock', 'add_wood', 'buy', 'sell']


def parse_vendor_intent(text: str) -> Optional[Tuple[VendorIntent, tuple]]:
    t = text.strip()
    m_stock = re.match(r"^(?:!stock|muestra\s+stock|ver\s+stock|dime\s+stock)(?:\s+(?:de\s+)?(\w+))?$", t, flags=re.IGNORECASE)
    if m_stock:
        return 'stock', ((m_stock.group(1) or 'wood').lower(),)
    m_stock_q = re.match(r"^(?:qu[eé]\s+stock\s+tienes\??|cu[aá]nt[oa]\s+(?:stock|madera|maderas)\s+(?:tienes|ten[ée]s)(?:\s+de\s+(\w+))?\??)$", t, flags=re.IGNORECASE)
    if m_stock_q:
        return 'stock_q', ((m_stock_q.group(1) or 'wood').lower(),)
    # Listado completo de items en venta
    m_list_1 = re.match(r"^(?:dime|di)\s+(?:todos\s+los\s+)?(?:items|ítems)\s+que\s+vendes\??$", t, flags=re.IGNORECASE)
    m_list_2 = re.match(r"^(?:qu[eé]|que)\s+(?:vendes|vend[eé]s|vendas|ofreces|ofrec[eé]s)\??$", t, flags=re.IGNORECASE)
    m_list_3 = re.match(r"^(?:lista|listado)\s+de\s+(?:items|ítems)(?:\s+a\s+la\s+venta)?\??$", t, flags=re.IGNORECASE)
    m_list_items = re.match(r"^(?:items|ítems)(?:\s+disponibles)?\s*\??$", t, flags=re.IGNORECASE)
    # Fallback: detectar 'que vendes' embebido, con texto antes/después y puntuación final opcional
    m_list_fallback = re.match(r"^.*\b(?:lista|listado|que|qu[eé]?)\b.*\b(?:vendes|vend[eé]s|vendas|ofreces|ofrec[eé]s)\b.*[\?\!\/\.]*$", t, flags=re.IGNORECASE)
    if m_list_1 or m_list_2 or m_list_3 or m_list_items or m_list_fallback:
        return 'stock_list', tuple()
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
