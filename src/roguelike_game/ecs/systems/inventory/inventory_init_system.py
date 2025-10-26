import os
import json

from .inventory_io import ensure_active_file, read_json_or
from .vendor_seed import VendorSupport
from .inventory_update_runner import run_inventory_init_update
from roguelike_game.ecs.components.inventory_component import InventoryComponent


class InventoryInitSystem:
    """
    Sistema ECS que inicializa inventarios para Player y NPCs desde plantillas por defecto
    y persiste el estado inicial en archivos JSON activos.
    """
    def __init__(self, perf_log=None,
                 default_monster_path: str = 'data/inventory/defaults/inventory_monsters.json',
                 active_monster_path: str = 'data/inventory/active/inventory_monsters.json',
                 default_player_path: str = 'data/inventory/defaults/inventory_player.json',
                 active_player_path: str = 'data/inventory/active/inventory_player.json',
                 default_neutral_path: str = 'data/inventory/defaults/inventory_neutrals.json',
                 active_neutral_path: str = 'data/inventory/active/inventory_neutrals.json',
                 schema_version: str = '1.0.0'):
        self.perf_log = perf_log
        self.default_monster_path = default_monster_path
        self.active_monster_path = active_monster_path
        self.default_player_path = default_player_path
        self.active_player_path = active_player_path
        self.default_neutral_path = default_neutral_path
        self.active_neutral_path = active_neutral_path
        self.schema_version = schema_version
        self.initialized = set()

        # Cargar plantillas por defecto
        with open(self.default_monster_path, 'r') as f:
            self.monster_templates = json.load(f)
        with open(self.default_player_path, 'r') as f:
            self.player_template = json.load(f)
        # Plantillas neutrales (si no existe archivo, usar vacío)
        try:
            with open(self.default_neutral_path, 'r') as f:
                self.neutral_templates = json.load(f)
        except Exception:
            self.neutral_templates = {}

        # Asegurar archivos activos existan
        ensure_active_file(self.active_monster_path, {})
        ensure_active_file(self.active_player_path, {})
        ensure_active_file(self.active_neutral_path, {})

        # Load active inventories into memory with fallback
        self.active_monsters = read_json_or(self.active_monster_path, {})
        self.active_players = read_json_or(self.active_player_path, {})
        self.active_neutrals = read_json_or(self.active_neutral_path, {})
        # Initialize dirty flags
        self.dirty_monsters = False
        self.dirty_players = False
        self.dirty_neutrals = False

        # Registry & Schemas
        self.vendors_registry_path = os.path.join('data', 'vendors', 'registry', 'vendors.json')
        self._vendors_registry = None
        self._vendors_registry_mtime = None
        self.inventory_seed_schema_path = os.path.join('schemas', 'vendors', 'InventorySeedSchema.json')
        self._inventory_seed_schema = None
        # Catálogo de ítems para sembrar stock comerciable en NPCs con chat
        self.items_catalog_path = os.path.join('data', 'items', 'items.json')
        self._items_catalog = None
        self._items_catalog_mtime = None

        # Helper para operaciones de vendors/semillas y catálogo de ítems
        self.vendor_support = VendorSupport(
            vendors_registry_path=self.vendors_registry_path,
            items_catalog_path=self.items_catalog_path,
            inventory_seed_schema_path=self.inventory_seed_schema_path,
        )

    
    def update(self, world, *args):
        # Delegar la lógica del update al runner especializado para mejorar legibilidad y testabilidad
        run_inventory_init_update(self, world, *args)

    # --- Helpers: Vendors Registry & Schemas -------------------------------
    def _ensure_seed_schema_loaded(self):
        if self._inventory_seed_schema is not None:
            return
        # Delegar en helper y reflejar caché local
        self.vendor_support._ensure_seed_schema_loaded()
        self._inventory_seed_schema = self.vendor_support._inventory_seed_schema

    def _load_vendors_registry(self):
        # Delegar en helper y reflejar caché local
        reg = self.vendor_support._load_vendors_registry()
        self._vendors_registry = reg
        # No exponemos mtime aquí; sólo mantenemos compatibilidad de retorno
        return reg

    def _get_vendor_entry(self, identity_key: str):
        # Delegar en helper, manteniendo firma
        return self.vendor_support.get_vendor_entry(identity_key)

    # --- Helpers: catálogo de ítems y siembra para comercio -----------------
    def _ensure_items_catalog_loaded(self):
        # Delegar en helper y reflejar caché local
        self.vendor_support._ensure_items_catalog_loaded()
        self._items_catalog = self.vendor_support._items_catalog

    def _maybe_seed_trader(self, eid: int, inv_comp: InventoryComponent, *, is_neutral: bool, active_store: dict, iid: str, allowed_ids: set[str] | None = None):
        """Garantiza que un NPC con chat tenga oro y algo de stock vendible.

        Reglas:
          - Asegura al menos MIN_GOLD de 'gold'.
          - Si no tiene ningún ítem vendible (excluyendo 'gold'), añade hasta MAX_SEED_ITEMS
            ítems stackeables del catálogo, con cantidades pequeñas.
        Persiste los cambios en el almacén activo correspondiente y marca dirty.
        """
        # Delegar en helper centralizado (mantiene persistencia y comportamiento)
        self.vendor_support.maybe_seed_trader(
            inv_comp,
            active_store=active_store,
            iid=iid,
            schema_version=self.schema_version,
            allowed_ids=allowed_ids,
        )
        if is_neutral:
            self.dirty_neutrals = True
        else:
            self.dirty_monsters = True
