from __future__ import annotations

import json
import logging
import os
from typing import Any, Dict, Optional

logger = logging.getLogger(__name__)


class EconomyService:
    """Vendor registry and economy groups (whitelist/blacklist/margins)."""

    def __init__(self, vendors_registry_path: str | None = None) -> None:
        self._vendors_registry_path = vendors_registry_path or os.path.join('data', 'vendors', 'registry', 'vendors.json')
        self._vendors_registry: Dict[str, Any] | None = None
        self._vendors_registry_mtime: float | None = None
        self._economy_cache: Dict[str, Dict[str, Any]] = {}

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

    # ------------------- Rules -------------------
    def is_allowed(self, world, vendor_eid: int, item_id: str, side: str) -> bool:
        entry = self.get_vendor_entry(world, vendor_eid)
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
