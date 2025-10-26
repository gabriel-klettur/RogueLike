from __future__ import annotations

import json
import logging
import os
from typing import Any, Dict, Optional
from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow

logger = logging.getLogger(__name__)


class EconomyService:
    """Vendor registry and economy groups (whitelist/blacklist/margins)."""

    def __init__(self, vendors_registry_path: str | None = None) -> None:
        self._vendors_registry_path = vendors_registry_path or os.path.join('data', 'vendors', 'registry', 'vendors.json')
        self._vendors_registry: Dict[str, Any] | None = None
        self._vendors_registry_mtime: float | None = None
        self._economy_cache: Dict[str, Dict[str, Any]] = {}
        # Cache de filtros por tipo resolvidos desde SQLite por vendor identity key
        self._type_filter_cache: Dict[str, set[str]] = {}

    # ------------------- Registry access -------------------
    def load_vendors_registry(self) -> Optional[Dict[str, Any]]:
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
                logger.exception("Failed to load vendors registry")
                self._vendors_registry = None
            self._vendors_registry_mtime = mtime
        return self._vendors_registry

    def get_vendor_identity_key(self, world, vendor_eid: int) -> Optional[str]:
        comps = world.components.get('Identity', {})
        ident = comps.get(vendor_eid)
        try:
            return str(ident.name).lower()
        except Exception:
            return None

    def get_vendor_entry(self, world, vendor_eid: int) -> Optional[Dict[str, Any]]:
        key = self.get_vendor_identity_key(world, vendor_eid)
        reg = self.load_vendors_registry()
        if not key or not isinstance(reg, dict):
            return None
        vendors = reg.get('vendors') or {}
        return vendors.get(key)

    # ------------------- Economy profile -------------------
    def _load_economy_profile(self, group: str | None) -> Optional[Dict[str, Any]]:
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
                logger.exception("Failed to load economy profile for group %s", group)
                prof = None
            self._economy_cache[group] = {'mtime': mtime, 'profile': prof}
        return self._economy_cache[group]['profile']

    # ------------------- Type-filter by SQLite -------------------
    def _determine_allowed_types(self, entry: Optional[Dict[str, Any]]) -> set[str]:
        """Infer allowed item 'type' values for a vendor.

        Current behavior: force {'food'} for ALL vendors.
        This is a temporary global default until more types are defined in the DB.
        """
        try:
            return {'food'}
        except Exception:
            # Fallback remains 'food' to keep safe default
            return {'food'}

    def get_allowed_item_ids_by_type(self, world, vendor_eid: int) -> Optional[set[str]]:
        """Return allowed item IDs for this vendor based on Items.type from SQLite.

        None means 'no restriction'. Otherwise, set of allowed item ids (lowercased).
        """
        key = self.get_vendor_identity_key(world, vendor_eid)
        if not key:
            return None
        if key in self._type_filter_cache:
            return self._type_filter_cache.get(key)
        entry = self.get_vendor_entry(world, vendor_eid)
        allowed_types = self._determine_allowed_types(entry)
        if not allowed_types:
            self._type_filter_cache[key] = None  # type: ignore
            return None
        ids: set[str] = set()
        # Consultar SQLite: escanear todo y aplicar filtro + heurística
        try:
            with session_scope() as s:
                rows = s.query(ItemRow).all()
                allowed_items_meta = []
                for r in rows:
                    try:
                        iid = str(getattr(r, 'id'))
                        tval = str(getattr(r, 'type', '') or '')
                        try:
                            nm = str(getattr(r, 'name', '') or '')
                        except Exception:
                            nm = ''
                        # Heurística ampliada para 'food' si la columna 'type' está vacía en el dataset importado
                        is_food = False
                        if 'food' in allowed_types:
                            if iid.lower().startswith('food_'):
                                is_food = True
                            # Revisar rutas de iconos por carpeta Cook
                            icon_small = str(getattr(r, 'icon_small', '') or '')
                            icon_large = str(getattr(r, 'icon_large', '') or '')
                            icon_json  = str(getattr(r, 'icon_json', '') or '')
                            icon_blob = (icon_small + ' ' + icon_large + ' ' + icon_json).lower()
                            if '/cook/' in icon_blob or '\\cook\\' in icon_blob:
                                is_food = True
                            # Palabras clave comunes en IDs/nombres para comidas importadas
                            kw = (
                                'borsh', 'borscht', 'varenyky', 'perogi', 'pierogi', 'paella', 'tortilla', 'completo', 'hakarl'
                            )
                            text_blob = (iid + ' ' + nm).lower()
                            if any(k in text_blob for k in kw):
                                is_food = True
                        # Regla principal: si type coincide, aceptar; si no, usar heurística anterior
                        if tval in allowed_types or is_food:
                            ids.add(iid.lower())
                            allowed_items_meta.append({'id': iid.lower(), 'name': nm, 'type': tval})
                    except Exception:
                        continue
        except Exception:
            logger.exception("Failed to resolve allowed item ids by type for vendor %s", key)
        try:
            scanned = len(rows) if 'rows' in locals() and isinstance(rows, list) else -1
            allowed_count = len(ids)
            logger.info(
                "[Economy][AllowedItems] vendor=%s types=%s scanned=%s allowed=%s",
                key, sorted(list(allowed_types)), scanned, allowed_count,
            )
            if 'allowed_items_meta' in locals() and allowed_items_meta:
                lines = []
                for m in sorted(allowed_items_meta, key=lambda x: x.get('id', ''))[:100]:
                    nm = m.get('name') or m.get('id')
                    lines.append(f"- {nm} ({m.get('id')}) type={m.get('type','')}")
                logger.info("[Economy][AllowedItems][Details]\n%s", "\n\n".join(lines))
        except Exception:
            pass
        self._type_filter_cache[key] = ids
        return ids

    def preload_allowed_ids(self, world, vendor_eid: int) -> None:
        """Warm up cache for allowed item ids (no-op if unrestricted)."""
        try:
            _ = self.get_allowed_item_ids_by_type(world, vendor_eid)
        except Exception:
            pass

    # ------------------- Rules -------------------
    def is_allowed(self, world, vendor_eid: int, item_id: str, side: str) -> bool:
        entry = self.get_vendor_entry(world, vendor_eid)
        # Filtro por tipo desde SQLite (permitir siempre 'gold')
        try:
            allowed_ids = self.get_allowed_item_ids_by_type(world, vendor_eid)
            if isinstance(allowed_ids, set):
                iid = (item_id or '').lower()
                if iid != 'gold' and iid not in allowed_ids:
                    return False
        except Exception:
            pass
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

    def apply_margins(self, world, vendor_eid: int, item_id: str, base_price: float, side: str) -> Optional[float]:
        entry = self.get_vendor_entry(world, vendor_eid)
        # Respetar filtro por tipo desde SQLite
        try:
            allowed_ids = self.get_allowed_item_ids_by_type(world, vendor_eid)
            if isinstance(allowed_ids, set):
                if (item_id or '').lower() != 'gold' and (item_id or '').lower() not in allowed_ids:
                    return None
        except Exception:
            pass
        group = entry.get('economy_group') if entry else None
        profile = self._load_economy_profile(group) if group else None
        if not isinstance(profile, dict):
            return base_price
        wl = profile.get('whitelist') or []
        bl = profile.get('blacklist') or []
        if item_id in bl:
            return None
        if wl and item_id not in wl:
            return None
        margins = profile.get('margins') or {}
        default_m = margins.get('default') or {}
        items_m = (margins.get('items') or {}).get(item_id) or {}
        mdef = float(default_m.get(side, 1.0)) if self._is_number(default_m.get(side, 1.0)) else 1.0
        mitem = float(items_m.get(side, mdef)) if self._is_number(items_m.get(side, mdef)) else mdef
        return float(base_price) * mitem

    @staticmethod
    def _is_number(x: Any) -> bool:
        try:
            float(x)
            return True
        except Exception:
            return False
