from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable, Dict, Optional, Tuple


@dataclass
class ToolExecResult:
    ok: bool
    message: str
    effects: Dict[str, Any]


class ToolRegistry:
    """Registro de tools disponibles por nombre y sus ejecutores.

    Esta versión inicial no tiene side-effects reales en ECS. Devuelve
    un diccionario de efectos para que la integración los aplique más adelante.
    """

    def __init__(self) -> None:
        self._execs: Dict[str, Callable[[Dict[str, Any]], ToolExecResult]] = {}
        self._register_defaults()

    def _register_defaults(self) -> None:
        # Vendor tools
        self.register("vendor.stock", self._exec_vendor_stock)
        self.register("vendor.buy", self._exec_vendor_buy)
        self.register("vendor.sell", self._exec_vendor_sell)

    def register(self, name: str, fn: Callable[[Dict[str, Any]], ToolExecResult]) -> None:
        self._execs[name] = fn

    def execute(self, name: str, args: Optional[Dict[str, Any]] = None) -> ToolExecResult:
        args = args or {}
        # Normalizar nombres: permitir 'vendor_buy' además de 'vendor.buy'
        name_norm = (name or '').replace('_', '.').strip()
        fn = self._execs.get(name_norm) or self._execs.get(name)
        if not fn:
            return ToolExecResult(ok=False, message=f"Tool desconocida: {name}", effects={})
        try:
            return fn(self._normalize_args(args))
        except Exception as e:
            return ToolExecResult(ok=False, message=f"Error en tool {name}: {e}", effects={})

    # --- Implementaciones dummy ---

    def _normalize_args(self, args: Dict[str, Any]) -> Dict[str, Any]:
        """Normaliza argumentos conversacionales.

        - item: acepta alias y los mapea a 'wooden'.
        - quantity: fuerza int >= 1 si es posible.
        """
        out = dict(args or {})
        # item aliases
        item = str(out.get('item', '') or '').lower().strip()
        aliases = {
            'wood': 'wooden',
            'wooden': 'wooden',
            'madera': 'wooden',
            'maderas': 'wooden',
        }
        if item:
            out['item'] = aliases.get(item, item)
        else:
            out['item'] = 'wooden'
        # quantity safe
        q = out.get('quantity', 1)
        try:
            q = int(q)
        except Exception:
            q = 1
        if q < 1:
            q = 1
        out['quantity'] = q
        return out

    def _exec_vendor_stock(self, args: Dict[str, Any]) -> ToolExecResult:
        # Placeholder: devolvería stock y precio.
        return ToolExecResult(
            ok=True,
            message="Stock: madera a 1 oro/unidad.",
            effects={"stock": {"wooden": {"price": 1, "qty": 999}}},
        )

    def _exec_vendor_buy(self, args: Dict[str, Any]) -> ToolExecResult:
        item = str(args.get("item", "wooden"))
        qty = int(args.get("quantity", 1))
        price = 1
        gold_delta = -price * qty
        msg = f"Compraste {qty} {item} por {price * qty} oro."
        return ToolExecResult(
            ok=True,
            message=msg,
            effects={
                "player_inventory_add": {item: qty},
                "player_gold_delta": gold_delta,
                "vendor_inventory_delta": {item: -qty},
            },
        )

    def _exec_vendor_sell(self, args: Dict[str, Any]) -> ToolExecResult:
        item = str(args.get("item", "wooden"))
        qty = int(args.get("quantity", 1))
        price = 1
        gold_delta = price * qty
        msg = f"Vendiste {qty} {item} por {price * qty} oro."
        return ToolExecResult(
            ok=True,
            message=msg,
            effects={
                "player_inventory_delta": {item: -qty},
                "player_gold_delta": gold_delta,
                "vendor_inventory_add": {item: qty},
            },
        )
