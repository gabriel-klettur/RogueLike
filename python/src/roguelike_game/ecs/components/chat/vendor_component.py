from dataclasses import dataclass, field
from typing import Dict


@dataclass
class VendorComponent:
    """
    Marca a una entidad como vendedor y define tarifas.

    - prices: mapa item_id -> precio por unidad en oro (item 'gold')
      Ejemplo inicial: { 'wood': 1 }  # 1 oro por unidad de madera

    La lógica de comercio (comprar/vender) aplica estas tarifas y realiza
    transacciones atómicas de inventario en VendorTradeSystem.
    """
    prices: Dict[str, int] = field(default_factory=lambda: {"wood": 1})
    currency_item_id: str = "gold"
