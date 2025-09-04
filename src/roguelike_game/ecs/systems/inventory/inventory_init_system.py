import os
import json
import random
import uuid
import jsonschema

from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.core.identity import Faction


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
        os.makedirs(os.path.dirname(self.active_monster_path), exist_ok=True)
        if not os.path.exists(self.active_monster_path):
            with open(self.active_monster_path, 'w') as f:
                json.dump({}, f, indent=2)
        os.makedirs(os.path.dirname(self.active_player_path), exist_ok=True)
        if not os.path.exists(self.active_player_path):
            with open(self.active_player_path, 'w') as f:
                json.dump({}, f, indent=2)
        os.makedirs(os.path.dirname(self.active_neutral_path), exist_ok=True)
        if not os.path.exists(self.active_neutral_path):
            with open(self.active_neutral_path, 'w') as f:
                json.dump({}, f, indent=2)

        # Load active inventories into memory with fallback
        try:
            with open(self.active_monster_path, 'r') as f:
                self.active_monsters = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError):
            self.active_monsters = {}
            with open(self.active_monster_path, 'w') as f:
                json.dump(self.active_monsters, f, indent=2)
        try:
            with open(self.active_player_path, 'r') as f:
                self.active_players = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError):
            self.active_players = {}
            with open(self.active_player_path, 'w') as f:
                json.dump(self.active_players, f, indent=2)
        try:
            with open(self.active_neutral_path, 'r') as f:
                self.active_neutrals = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError):
            self.active_neutrals = {}
            with open(self.active_neutral_path, 'w') as f:
                json.dump(self.active_neutrals, f, indent=2)
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

    
    def update(self, world, *args):
        comps = world.components
        player_tag_store = comps.get('PlayerTagComponent', {})
        npc_tag_store = comps.get('NPCTagComponent', {})
        instance_store = comps.get('MonsterInstanceComponent', {})

        # Cargar datos activos
        # Cargar datos activos
        # Use in-memory active_monsters
        active_monsters = self.active_monsters
        # Reset dirty flag for monsters
        self.dirty_monsters = False
        # Use in-memory active_players
        active_players = self.active_players
        # Reset dirty flag for players
        self.dirty_players = False
        # Use in-memory active_neutrals
        active_neutrals = self.active_neutrals
        # Reset dirty flag for neutrals
        self.dirty_neutrals = False

        # Inicializar jugadores
        for eid in list(player_tag_store.keys()):
            if eid in self.initialized:
                continue
            key = str(eid)
            # Si ya existen componentes (cargados desde un save), NO sobrescribirlos
            existing_inv = world.components.get('InventoryComponent', {}).get(eid)
            existing_xp  = world.components.get('ExperienceComponent', {}).get(eid)

            # Inventario: solo crear/asignar si no existe ya
            if existing_inv is None:
                # Cargar inventario persistido de activos si existe
                if key in active_players:
                    pdata = active_players[key]
                    # Normalizar player_id inexistente o inválido
                    pid = pdata.get('player_id')
                    try:
                        uuid.UUID(str(pid)) if pid else (_ for _ in ()).throw(ValueError())
                    except Exception:
                        pid = str(uuid.uuid4())
                        pdata['player_id'] = pid
                        self.dirty_players = True
                    inv_comp = InventoryComponent(
                        capacity=self.player_template.get('capacity', 20),
                        player_id=pid
                    )
                    for slot in pdata.get('slots', []):
                        if slot:
                            inv_comp.add(slot['item'], slot['quantity'])
                else:
                    # Crear InventoryComponent con plantilla por defecto
                    capacity = self.player_template.get('capacity', 20)
                    # Normalizar/generar player_id si plantilla no lo define
                    player_id = self.player_template.get('player_id')
                    try:
                        uuid.UUID(str(player_id)) if player_id else (_ for _ in ()).throw(ValueError())
                    except Exception:
                        player_id = str(uuid.uuid4())
                    inv_comp = InventoryComponent(capacity=capacity, player_id=player_id)
                    for slot in self.player_template.get('slots', []):
                        if slot:
                            inv_comp.add(slot['item'], slot['quantity'])
                    # Persistir inicial
                    active_players[key] = {
                        'player_id': player_id,
                        'slots': inv_comp.serialize().get('slots'),
                        'schema_version': self.schema_version
                    }
                    self.dirty_players = True
                world.components['InventoryComponent'][eid] = inv_comp

            # Experiencia: solo crear por defecto si no existe ya
            if existing_xp is None:
                world.components['ExperienceComponent'][eid] = ExperienceComponent()
            self.initialized.add(eid)

        # Inicializar NPCs (hostiles y neutrales)
        for eid in list(npc_tag_store.keys()):
            inst = instance_store.get(eid)
            if not inst:
                continue
            iid = inst.instance_id
            # Saltar si ya inicializado
            if iid in self.initialized:
                continue
            if eid in self.initialized:
                continue
            # Determinar plantilla a partir de Identity.name y facción
            identity = comps.get('Identity', {}).get(eid)
            template_key = identity.name.lower() if identity else None
            is_neutral = False
            try:
                is_neutral = (identity is not None and identity.faction == Faction.NEUTRAL)
            except Exception:
                is_neutral = False
            # Seleccionar plantilla y almacén activo según facción
            template = None
            if template_key:
                template = (self.neutral_templates.get(template_key) if is_neutral else self.monster_templates.get(template_key))
            if not template:
                # Si no hay plantilla (p.ej., neutrales sin entry), continuar pero permitir semillas para vendors
                template = {}
            active_store = active_neutrals if is_neutral else active_monsters

            template_id = template.get('template_id')
            if iid in active_store:
                # Cargar inventario activo existente
                saved = active_store.get(iid, {})
                inv_comp = InventoryComponent(player_id=saved.get('template_id', template_id))
                for slot in saved.get('slots', []):
                    if slot:
                        inv_comp.add(slot['item'], slot.get('quantity', 0))
            else:
                # Si no hay activo persistido, intentar cargar semilla específica de vendor
                loaded_from_seed = False
                try:
                    identity_key = (template_key or '').lower()
                    is_vendor = (eid in comps.get('VendorComponent', {})) or ('vendor' in identity_key)
                    if is_vendor and identity_key:
                        # Cargar registry para obtener semilla específica y grupo
                        candidates = []
                        entry = self._get_vendor_entry(identity_key)
                        if entry:
                            spath = entry.get('seed_specific')
                            if spath:
                                candidates.append(spath)
                            group = entry.get('seed_group') or entry.get('economy_group')
                            if group:
                                seed_group = os.path.join('data', 'vendors', 'inventory_seed', 'groups', f'{group}_default.json')
                                candidates.append(seed_group)
                        # Fallback heurístico: semilla específica basada en identity
                        candidates.append(os.path.join('data', 'vendors', 'inventory_seed', f'inventory_{identity_key}.json'))
                        for path in candidates:
                            if os.path.exists(path) and os.path.getsize(path) > 0:
                                with open(path, 'r', encoding='utf-8') as f:
                                    data = json.load(f)
                                # Validar contra schema de semillas si está disponible
                                try:
                                    self._ensure_seed_schema_loaded()
                                    if self._inventory_seed_schema is not None:
                                        jsonschema.validate(instance=data, schema=self._inventory_seed_schema)
                                except Exception:
                                    # Semilla inválida; intentar siguiente candidato
                                    continue
                                slots = data.get('slots')
                                if isinstance(slots, list):
                                    file_tid = data.get('template_id')
                                    # Normalizar template_id si falta/incorrecto
                                    try:
                                        uuid.UUID(str(file_tid)) if file_tid else (_ for _ in ()).throw(ValueError())
                                    except Exception:
                                        file_tid = template_id or str(uuid.uuid4())
                                    inv_comp = InventoryComponent(player_id=file_tid)
                                    for slot in slots:
                                        if slot:
                                            inv_comp.add(slot.get('item'), int(slot.get('quantity', 0)))
                                    # Persistir inventario inicializado en activos para esta instancia
                                    active_store[iid] = {
                                        'template_id': file_tid,
                                        'slots': inv_comp.serialize().get('slots'),
                                        'schema_version': self.schema_version
                                    }
                                    if is_neutral:
                                        self.dirty_neutrals = True
                                    else:
                                        self.dirty_monsters = True
                                    loaded_from_seed = True
                                    break
                except Exception:
                    # Si hay cualquier problema al cargar semillas, continuamos con plantilla por defecto
                    pass
                if not loaded_from_seed:
                    # Generar ítems según rangos y probabilidades (plantilla por defecto)
                    inv_comp = InventoryComponent(player_id=template_id)
                    for entry in template.get('inventory', []):
                        if random.random() <= entry.get('chance', 1.0):
                            qty = random.randint(entry.get('min', 1), entry.get('max', 1))
                            if qty > 0:
                                inv_comp.add(entry['item'], qty)
                    # Persistir inventario generado
                    active_store[iid] = {
                        'template_id': template_id,
                        'slots': inv_comp.serialize().get('slots'),
                        'schema_version': self.schema_version
                    }
                    if is_neutral:
                        self.dirty_neutrals = True
                    else:
                        self.dirty_monsters = True
            # Asignar componente e inicializar marcado
            world.components['InventoryComponent'][eid] = inv_comp
            self.initialized.add(eid)

        # Remove entries for monsters/neutrals no longer present
        current_npc_keys = set(inst.instance_id for eid, inst in instance_store.items() if eid in npc_tag_store)
        for key in list(active_monsters.keys()):
            if key not in current_npc_keys:
                active_monsters.pop(key)
                self.dirty_monsters = True
        for key in list(active_neutrals.keys()):
            if key not in current_npc_keys:
                active_neutrals.pop(key)
                self.dirty_neutrals = True
        # Guardar archivos activos solo si hay cambios
        if self.dirty_monsters:
            with open(self.active_monster_path, 'w') as f:
                json.dump(self.active_monsters, f, indent=2)
        if self.dirty_players:
            with open(self.active_player_path, 'w') as f:
                json.dump(self.active_players, f, indent=2)
        if self.dirty_neutrals:
            with open(self.active_neutral_path, 'w') as f:
                json.dump(self.active_neutrals, f, indent=2)

    # --- Helpers: Vendors Registry & Schemas -------------------------------
    def _ensure_seed_schema_loaded(self):
        if self._inventory_seed_schema is not None:
            return
        try:
            with open(self.inventory_seed_schema_path, 'r', encoding='utf-8') as f:
                self._inventory_seed_schema = json.load(f)
        except Exception:
            self._inventory_seed_schema = None

    def _load_vendors_registry(self):
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
                with open(path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                self._vendors_registry = data
            except Exception:
                self._vendors_registry = None
            self._vendors_registry_mtime = mtime
        return self._vendors_registry

    def _get_vendor_entry(self, identity_key: str):
        reg = self._load_vendors_registry()
        if not isinstance(reg, dict):
            return None
        vendors = reg.get('vendors') or {}
        return vendors.get(identity_key)
