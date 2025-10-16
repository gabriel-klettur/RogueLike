import logging
from typing import Any, Optional
from .services import (
    PriceService,
    EconomyService,
    PersonaService,
    IdNormalizer,
    get_transfer_system,
)

logger = logging.getLogger(__name__)

class VendorTradeSystem:
    """
    Maneja operaciones de comercio con vendedores usando InventoryTransferSystem.

    Métodos públicos:
      - buy(world, vendor_eid, item_id, qty)
      - sell(world, vendor_eid, item_id, qty)
    Devuelven un string con el resultado para mostrar en el chat.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Servicios especializados para separar responsabilidades
        self._price_service = PriceService()
        self._economy_service = EconomyService()
        self._persona_service = PersonaService()
        self._id_normalizer = IdNormalizer(self._price_service)
     
    def update(self, world, *args):
        # No-op
        return

    # --- API -----------------------------------------------------------------
    def buy(self, world, vendor_eid: int, item_id: str, qty: int) -> str:
        """El jugador compra `qty` del `item_id` al vendedor.
        Mueve item del vendedor -> jugador, y oro del jugador -> vendedor.
        """
        if qty <= 0:
            return "Cantidad inválida."
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return "No hay jugador activo."
        item_id, currency_id = self._id_normalizer.normalize_ids(world, vendor_eid, item_id)
        price = self._get_price(world, vendor_eid, item_id, op='buy')
        if price is None:
            return "Ese artículo no está a la venta."
        total = price * qty
        its = self._get_transfer_system(world)
        invs = world.components.get('InventoryComponent', {})
        v_inv = invs.get(vendor_eid)
        p_inv = invs.get(player_eid)
        if not v_inv or not p_inv:
            return "Falta inventario en vendedor o jugador."
        # Comprobaciones previas
        if not v_inv.has(item_id, qty):
            return f"No tengo suficiente stock de {item_id}."
        if not p_inv.has(currency_id, total):
            return f"No tienes {total} {currency_id}."
        # 1) Entregar items al jugador
        try:
            its.transfer(world, item_id, qty, vendor_eid, player_eid)
        except Exception as e:
            logger.exception("Fallo al transferir item vendor->player")
            return f"No pude entregarte {qty}x {item_id}: {e}"
        # 2) Cobrar oro y rollback si falla
        try:
            its.transfer(world, currency_id, total, player_eid, vendor_eid)
        except Exception as e:
            logger.exception("Fallo al cobrar oro, realizando rollback de item")
            # Rollback de items
            try:
                its.transfer(world, item_id, qty, player_eid, vendor_eid)
            except Exception:
                logger.error("Rollback de item falló; estado inconsistente")
            return f"Transacción cancelada: no pude cobrar {total} {currency_id}."
        return f"Hecho. Compraste {qty}x {item_id} por {total} {currency_id}."

    def sell(self, world, vendor_eid: int, item_id: str, qty: int) -> str:
        """El jugador vende `qty` del `item_id` al vendedor.
        Mueve item del jugador -> vendedor, y oro del vendedor -> jugador.
        """
        if qty <= 0:
            return "Cantidad inválida."
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return "No hay jugador activo."
        item_id, currency_id = self._id_normalizer.normalize_ids(world, vendor_eid, item_id)
        price = self._get_price(world, vendor_eid, item_id, op='sell')
        if price is None:
            return "No compro ese artículo."
        total = price * qty
        its = self._get_transfer_system(world)
        invs = world.components.get('InventoryComponent', {})
        v_inv = invs.get(vendor_eid)
        p_inv = invs.get(player_eid)
        if not v_inv or not p_inv:
            return "Falta inventario en vendedor o jugador."
        # Comprobaciones previas
        if not p_inv.has(item_id, qty):
            return f"No tienes {qty}x {item_id}."
        if not v_inv.has(currency_id, total):
            return f"El vendedor no tiene suficiente {currency_id} para pagarte."
        # 1) Recibir items del jugador
        try:
            its.transfer(world, item_id, qty, player_eid, vendor_eid)
        except Exception as e:
            logger.exception("Fallo al recibir item player->vendor")
            return f"No pude recibir {qty}x {item_id}: {e}"
        # 2) Pagar oro y rollback si falla
        try:
            its.transfer(world, currency_id, total, vendor_eid, player_eid)
        except Exception as e:
            logger.exception("Fallo al pagar oro, realizando rollback de item")
            # Rollback de items
            try:
                its.transfer(world, item_id, qty, vendor_eid, player_eid)
            except Exception:
                logger.error("Rollback de item falló; estado inconsistente")
            return f"Transacción cancelada: no pude pagarte {total} {currency_id}."
        return f"Hecho. Vendiste {qty}x {item_id} por {total} {currency_id}."

    def restock(self, world, vendor_eid: int, item_id: str, qty: int) -> str:
        """Incrementa el stock del vendedor en `qty` unidades del `item_id`.
        Uso: utilitario para debug o herramientas administrativas.
        """
        if qty <= 0:
            return "Cantidad inválida."
        item_id, _ = self._id_normalizer.normalize_ids(world, vendor_eid, item_id)
        invs = world.components.get('InventoryComponent', {})
        inv = invs.get(vendor_eid)
        if not inv:
            return "El vendedor no tiene inventario."
        try:
            ok = inv.add(item_id, qty)
            if not ok:
                return "Sin espacio para añadir stock."
            return f"Stock actualizado: +{qty} {item_id}."
        except Exception as e:
            logger.exception("Fallo en restock")
            return f"No pude actualizar stock: {e}"

    def get_stock(self, world, vendor_eid: int, item_id: str) -> int:
        """Devuelve el stock actual del `item_id` en el vendedor."""
        item_id, _ = self._id_normalizer.normalize_ids(world, vendor_eid, item_id)
        invs = world.components.get('InventoryComponent', {})
        inv = invs.get(vendor_eid)
        if not inv:
            return 0
        try:
            total = 0
            for st in getattr(inv, 'slots', []) or []:
                if st and str(getattr(st, 'item_id', '')).lower() == item_id:
                    total += int(getattr(st, 'quantity', 0) or 0)
            return total
        except Exception:
            return 0

    # --- Helpers --------------------------------------------------------------
    def _get_transfer_system(self, world):
        # Delegar a fachada para mantener única responsabilidad
        # Importar desde el módulo para respetar monkeypatch en tests
        from . import services as _services
        return _services.get_transfer_system(world)

    def _get_price(self, world, vendor_eid: int, item_id: str, op: Optional[str] = None) -> Optional[float]:
        """Determina el precio final respetando overrides, economía y negociación."""
        side = (op or '').lower()
        if side not in ('buy', 'sell'):
            side = 'buy'
        # 0) Regla de economía: permitido
        if not self._economy_service.is_allowed(world, vendor_eid, item_id, side):
            return None
        # 1) Override del vendedor
        comps = world.components.get('VendorComponent', {})
        vc = comps.get(vendor_eid)
        if vc:
            prices = getattr(vc, 'prices', {}) or {}
            if item_id in prices:
                v = prices.get(item_id)
                if isinstance(v, (int, float)):
                    return float(v)
                if isinstance(v, dict):
                    vv = v.get(side)
                    return float(vv) if self._is_number(vv) else None
        # 2) Precio global
        base = self._price_service.get_global_price(item_id, side)
        if base is None:
            return None
        # 3) Márgenes de economía
        adjusted = self._economy_service.apply_margins(world, vendor_eid, item_id, base, side)
        # 4) Negociación por persona
        adjusted = self._persona_service.apply_negotiation(world, vendor_eid, item_id, adjusted, side)
        return adjusted

    # Precios globales manejados por PriceService

    # Fallbacks de catálogo manejados por PriceService

    @staticmethod
    def _is_number(x):
        try:
            float(x)
            return True
        except Exception:
            return False

    # Normalización de IDs manejada por IdNormalizer

    # Registro de vendors y economía manejados por EconomyService

    # Negociación por persona manejada por PersonaService
