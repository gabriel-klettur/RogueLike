import logging
import os
import json
import jsonschema

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
        # Caché de precios globales cargados desde archivo
        self._prices_path = os.path.join('data', 'items', 'items_price.json')
        self._global_prices = None  # dict[str, number]
        self._global_prices_mtime = None
        # Schema de precios
        self._prices_schema_path = os.path.join('schemas', 'items', 'ItemsPriceSchema.json')
        self._prices_schema = None
        # Vendors registry y economía
        self._vendors_registry_path = os.path.join('data', 'vendors', 'registry', 'vendors.json')
        self._vendors_registry = None
        self._vendors_registry_mtime = None
        # Caché de perfiles de economía por grupo: { group: { 'mtime': float, 'profile': dict } }
        self._economy_cache = {}

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
        item_id, currency_id = self._normalize_ids(world, vendor_eid, item_id)
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
        item_id, currency_id = self._normalize_ids(world, vendor_eid, item_id)
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
        item_id, _ = self._normalize_ids(world, vendor_eid, item_id)
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
        item_id, _ = self._normalize_ids(world, vendor_eid, item_id)
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
        for s in getattr(world, 'update_systems', []):
            if type(s).__name__ == 'InventoryTransferSystem':
                return s
        # Fallback inusual: crear uno si no existiera
        from roguelike_game.ecs.systems.inventory.inventory_transfer_system import InventoryTransferSystem
        inst = InventoryTransferSystem()
        world.update_systems.append(inst)
        return inst

    def _get_price(self, world, vendor_eid: int, item_id: str, op: str | None = None):
        """Obtiene precio para `item_id`.
        Prioridad: VendorComponent.prices > precios globales. Acepta number o {buy,sell}.
        `op` debe ser 'buy' o 'sell' para elegir el lado.
        """
        side = (op or '').lower()
        if side not in ('buy', 'sell'):
            side = 'buy'
        # Primero, comprobar si está permitido por perfiles de economía
        if not self._is_allowed(world, vendor_eid, item_id, side):
            return None
        # 1) Precio específico del vendedor (override)
        comps = world.components.get('VendorComponent', {})
        vc = comps.get(vendor_eid)
        override_used = False
        if vc:
            prices = getattr(vc, 'prices', {}) or {}
            if item_id in prices:
                v = prices.get(item_id)
                if isinstance(v, (int, float)):
                    return float(v)
                if isinstance(v, dict):
                    vv = v.get(side)
                    return float(vv) if self._is_number(vv) else None
        # 2) Precio global desde archivo
        base = self._get_global_price(item_id, side)
        if base is None:
            return None
        # 3) Aplicar márgenes del perfil de economía (solo si no hay override explícito)
        adjusted = self._apply_economy_margins(world, vendor_eid, item_id, base, side)
        return adjusted

    # --- Precios globales ---------------------------------------------------
    def _get_global_price(self, item_id: str, side: str):
        try:
            self._ensure_prices_loaded()
            if isinstance(self._global_prices, dict):
                entry = self._global_prices.get(item_id)
                if isinstance(entry, dict):
                    v = entry.get(side)
                    return float(v) if self._is_number(v) else None
        except Exception:
            pass
        return None

    def _ensure_prices_loaded(self):
        path = self._prices_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            # Si no existe, dejar caché vacía
            self._global_prices = {}
            self._global_prices_mtime = None
            return
        # Recargar si no hay caché o cambió el archivo
        if self._global_prices is None or self._global_prices_mtime != mtime:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                # Validar contra schema si existe
                self._ensure_prices_schema_loaded()
                try:
                    if self._prices_schema is not None:
                        jsonschema.validate(instance=data, schema=self._prices_schema)
                except Exception:
                    # Si el schema falla, no derribar el sistema; dejar sin precios
                    data = {}
                # Esperamos un mapa item_id -> precio (number) o {buy,sell}
                parsed = {}
                if isinstance(data, dict):
                    for k, v in data.items():
                        key = str(k)
                        if self._is_number(v):
                            fv = float(v)
                            parsed[key] = {'buy': fv, 'sell': fv}
                        elif isinstance(v, dict):
                            buy_v = v.get('buy', v.get('price', None))
                            sell_v = v.get('sell', v.get('price', None))
                            entry = {}
                            if self._is_number(buy_v):
                                entry['buy'] = float(buy_v)
                            if self._is_number(sell_v):
                                entry['sell'] = float(sell_v)
                            if entry:
                                # Si falta alguna cara, replicar la existente
                                if 'buy' not in entry and 'sell' in entry:
                                    entry['buy'] = entry['sell']
                                if 'sell' not in entry and 'buy' in entry:
                                    entry['sell'] = entry['buy']
                                parsed[key] = entry
                self._global_prices = parsed
            except Exception:
                # Si hay error, no derribar el sistema de comercio
                self._global_prices = {}
                self._global_prices_mtime = mtime
        else:
            # No recarga necesaria
            pass

    @staticmethod
    def _is_number(x):
        try:
            float(x)
            return True
        except Exception:
            return False

    def _normalize_ids(self, world, vendor_eid: int, item_id: str):
        """Devuelve (item_id_normalizado, currency_id). Acepta alias como 'wooden' y 'oro'."""
        comps = world.components.get('VendorComponent', {})
        vc = comps.get(vendor_eid)
        currency = getattr(vc, 'currency_item_id', 'gold') if vc else 'gold'
        iid = (item_id or '').lower()
        if iid in ('wooden', 'madera'):
            iid = 'wood'
        if currency.lower() in ('oro',):
            currency = 'gold'
        return iid, currency

    # --- Vendors registry y economía ---------------------------------------
    def _ensure_prices_schema_loaded(self):
        if self._prices_schema is not None:
            return
        try:
            with open(self._prices_schema_path, 'r', encoding='utf-8') as f:
                self._prices_schema = json.load(f)
        except Exception:
            self._prices_schema = None

    def _load_vendors_registry(self):
        path = self._vendors_registry_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._vendors_registry = None
            self._vendors_registry_mtime = None
            return None
        if self._vendors_registry is None or self._vendors_registry_mtime != mtime:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                self._vendors_registry = data
            except Exception:
                self._vendors_registry = None
            self._vendors_registry_mtime = mtime
        return self._vendors_registry

    def _get_vendor_identity_key(self, world, vendor_eid: int):
        comps = world.components.get('Identity', {})
        ident = comps.get(vendor_eid)
        try:
            return ident.name.lower()
        except Exception:
            return None

    def _get_vendor_entry(self, world, vendor_eid: int):
        key = self._get_vendor_identity_key(world, vendor_eid)
        reg = self._load_vendors_registry()
        if not key or not isinstance(reg, dict):
            return None
        vendors = reg.get('vendors') or {}
        return vendors.get(key)

    def _load_economy_profile(self, group: str):
        if not group:
            return None
        cache = self._economy_cache.get(group)
        path = os.path.join('data', 'vendors', 'economy', 'groups', f'{group}.json')
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._economy_cache[group] = {'mtime': None, 'profile': None}
            return None
        if (not cache) or cache.get('mtime') != mtime:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    prof = json.load(f)
            except Exception:
                prof = None
            self._economy_cache[group] = {'mtime': mtime, 'profile': prof}
        return self._economy_cache[group]['profile']

    def _is_allowed(self, world, vendor_eid: int, item_id: str, side: str) -> bool:
        entry = self._get_vendor_entry(world, vendor_eid)
        group = entry.get('economy_group') if entry else None
        profile = self._load_economy_profile(group) if group else None
        if not isinstance(profile, dict):
            return True
        wl = profile.get('whitelist') or []
        bl = profile.get('blacklist') or []
        if item_id in bl:
            return False
        if wl:
            return item_id in wl
        return True

    def _apply_economy_margins(self, world, vendor_eid: int, item_id: str, base_price: float, side: str) -> float | None:
        entry = self._get_vendor_entry(world, vendor_eid)
        group = entry.get('economy_group') if entry else None
        profile = self._load_economy_profile(group) if group else None
        if not isinstance(profile, dict):
            return base_price
        # If not allowed, disallow
        wl = profile.get('whitelist') or []
        bl = profile.get('blacklist') or []
        if item_id in bl:
            return None
        if wl and item_id not in wl:
            return None
        margins = profile.get('margins') or {}
        default_m = margins.get('default') or {}
        items_m = (margins.get('items') or {}).get(item_id) or {}
        # Item margin overrides default
        mdef = float(default_m.get(side, 1.0)) if self._is_number(default_m.get(side, 1.0)) else 1.0
        mitem = float(items_m.get(side, mdef)) if self._is_number(items_m.get(side, mdef)) else mdef
        return float(base_price) * mitem
