"""Vendor seeding helpers for NPC inventories.

Encapsulates vendor registry reading, inventory seed schema validation,
items catalog loading, and trader stock seeding behaviors.
"""
from __future__ import annotations

import json
import os
import random
import uuid
from typing import Any, Dict, Optional

import jsonschema

from roguelike_game.ecs.components.inventory_component import InventoryComponent


class VendorSupport:
    """Helper for vendor-related inventory operations.

    Manages cached files and provides APIs to:
      - Validate inventory seed files against a JSON Schema.
      - Resolve vendor entries from a registry.
      - Build an InventoryComponent from a seed file.
      - Seed trader inventory with gold and basic stock when empty.
    """

    def __init__(
        self,
        *,
        vendors_registry_path: str,
        items_catalog_path: str,
        inventory_seed_schema_path: str,
    ) -> None:
        self.vendors_registry_path = vendors_registry_path
        self.items_catalog_path = items_catalog_path
        self.inventory_seed_schema_path = inventory_seed_schema_path

        self._vendors_registry: Optional[Dict[str, Any]] = None
        self._vendors_registry_mtime: Optional[float] = None
        self._items_catalog: Optional[Dict[str, Any]] = None
        self._items_catalog_mtime: Optional[float] = None
        self._inventory_seed_schema: Optional[Dict[str, Any]] = None

    # ---- Registry ---------------------------------------------------------
    def _load_vendors_registry(self) -> Optional[Dict[str, Any]]:
        path = self.vendors_registry_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._vendors_registry = None
            self._vendors_registry_mtime = None
            return None
        if self._vendors_registry is None or self._vendors_registry_mtime != mtime:
            try:
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                self._vendors_registry = data
            except Exception:
                self._vendors_registry = None
            self._vendors_registry_mtime = mtime
        return self._vendors_registry

    def get_vendor_entry(self, identity_key: str) -> Optional[Dict[str, Any]]:
        reg = self._load_vendors_registry()
        if not isinstance(reg, dict):
            return None
        vendors = reg.get("vendors") or {}
        return vendors.get(identity_key)

    # ---- Schema -----------------------------------------------------------
    def _ensure_seed_schema_loaded(self) -> None:
        if self._inventory_seed_schema is not None:
            return
        try:
            with open(self.inventory_seed_schema_path, "r", encoding="utf-8") as f:
                self._inventory_seed_schema = json.load(f)
        except Exception:
            self._inventory_seed_schema = None

    # ---- Items Catalog ----------------------------------------------------
    def _ensure_items_catalog_loaded(self) -> None:
        path = self.items_catalog_path
        try:
            st = os.stat(path)
            mtime = st.st_mtime
        except FileNotFoundError:
            self._items_catalog = {}
            self._items_catalog_mtime = None
            return
        if self._items_catalog is None or self._items_catalog_mtime != mtime:
            try:
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                self._items_catalog = data if isinstance(data, dict) else {}
            except Exception:
                self._items_catalog = {}
            self._items_catalog_mtime = mtime

    # ---- Build inventory from seed ---------------------------------------
    def try_build_inventory_from_seed(
        self,
        identity_key: str,
        template_id: Optional[str],
    ) -> Optional[InventoryComponent]:
        """Try to build an inventory for a vendor using available seed files.

        Returns an InventoryComponent if a valid seed is found; otherwise None.
        """
        if not identity_key:
            return None
        candidates = []
        entry = self.get_vendor_entry(identity_key)
        if entry:
            spath = entry.get("seed_specific")
            if spath:
                candidates.append(spath)
            group = entry.get("seed_group") or entry.get("economy_group")
            if group:
                seed_group = os.path.join(
                    "data", "vendors", "inventory_seed", "groups", f"{group}_default.json"
                )
                candidates.append(seed_group)
        # Heuristic fallback by identity key
        candidates.append(
            os.path.join("data", "vendors", "inventory_seed", f"inventory_{identity_key}.json")
        )

        for path in candidates:
            try:
                if not (os.path.exists(path) and os.path.getsize(path) > 0):
                    continue
                with open(path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                # Validate against schema if available
                self._ensure_seed_schema_loaded()
                if self._inventory_seed_schema is not None:
                    jsonschema.validate(instance=data, schema=self._inventory_seed_schema)
                slots = data.get("slots")
                if not isinstance(slots, list):
                    continue
                file_tid = data.get("template_id")
                # Normalize template id
                try:
                    uuid.UUID(str(file_tid)) if file_tid else (_ for _ in ()).throw(ValueError())
                except Exception:
                    file_tid = template_id or str(uuid.uuid4())
                inv_comp = InventoryComponent(player_id=file_tid)
                for slot in slots:
                    if slot:
                        inv_comp.add(slot.get("item"), int(slot.get("quantity", 0)))
                return inv_comp
            except Exception:
                # Skip invalid candidates and continue
                continue
        return None

    # ---- Trader seeding ---------------------------------------------------
    def maybe_seed_trader(
        self,
        inv_comp: InventoryComponent,
        *,
        active_store: Dict[str, Any],
        iid: str,
        schema_version: str,
    ) -> bool:
        """Ensure a trader has minimum gold and some stock; persist to active store.

        Returns True if active_store was updated (always True for idempotent write).
        """
        MIN_GOLD = 50
        MAX_SEED_ITEMS = 3
        # Ensure minimum gold
        try:
            if not inv_comp.has("gold", MIN_GOLD):
                inv_comp.add("gold", MIN_GOLD)
        except Exception:
            # If capability missing or error, ignore
            pass
        # If no vendable stock (excluding gold), seed from items catalog
        try:
            has_stock = any(
                st is not None and getattr(st, "item_id", "") != "gold"
                for st in getattr(inv_comp, "slots", []) or []
            )
        except Exception:
            has_stock = False
        if not has_stock:
            self._ensure_items_catalog_loaded()
            cat = self._items_catalog or {}
            candidates = []
            for iid_item, node in cat.items():
                if not isinstance(node, dict):
                    continue
                if iid_item in {"gold", "experience_orb"}:
                    continue
                if node.get("quest_id"):
                    continue
                if bool(node.get("stackable", False)):
                    candidates.append((iid_item, int(node.get("max_stack", 10) or 10)))
            random.shuffle(candidates)
            to_add = candidates[:MAX_SEED_ITEMS]
            for item_id, max_stack in to_add:
                qty = max(1, min(max_stack, random.randint(1, min(5, max_stack if max_stack > 0 else 5))))
                inv_comp.add(item_id, qty)
        # Persist
        entry_prev = active_store.get(iid, {}) or {}
        template_id = entry_prev.get("template_id", inv_comp.player_id)
        active_store[iid] = {
            "template_id": template_id,
            "slots": inv_comp.serialize().get("slots"),
            "schema_version": schema_version,
        }
        return True
