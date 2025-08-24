import os
import json
import random
import uuid

from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent
from roguelike_game.ecs.components.experience_component import ExperienceComponent


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
                 schema_version: str = '1.0.0'):
        self.perf_log = perf_log
        self.default_monster_path = default_monster_path
        self.active_monster_path = active_monster_path
        self.default_player_path = default_player_path
        self.active_player_path = active_player_path
        self.schema_version = schema_version
        self.initialized = set()

        # Cargar plantillas por defecto
        with open(self.default_monster_path, 'r') as f:
            self.monster_templates = json.load(f)
        with open(self.default_player_path, 'r') as f:
            self.player_template = json.load(f)

        # Asegurar archivos activos existan
        os.makedirs(os.path.dirname(self.active_monster_path), exist_ok=True)
        if not os.path.exists(self.active_monster_path):
            with open(self.active_monster_path, 'w') as f:
                json.dump({}, f, indent=2)
        os.makedirs(os.path.dirname(self.active_player_path), exist_ok=True)
        if not os.path.exists(self.active_player_path):
            with open(self.active_player_path, 'w') as f:
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
        # Initialize dirty flags
        self.dirty_monsters = False
        self.dirty_players = False

    
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

        # Inicializar NPCs
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
            # Determinar plantilla a partir de Identity.name
            identity = comps.get('Identity', {}).get(eid)
            template_key = identity.name.lower() if identity else None
            template = self.monster_templates.get(template_key)
            if not template:
                continue
            template_id = template.get('template_id')
            if iid in active_monsters:
                # Cargar inventario activo existente
                saved = active_monsters.get(iid, {})
                inv_comp = InventoryComponent(player_id=saved.get('template_id', template_id))
                for slot in saved.get('slots', []):
                    if slot:
                        inv_comp.add(slot['item'], slot.get('quantity', 0))
            else:
                # Generar ítems según rangos y probabilidades
                inv_comp = InventoryComponent(player_id=template_id)
                for entry in template.get('inventory', []):
                    if random.random() <= entry.get('chance', 1.0):
                        qty = random.randint(entry.get('min', 1), entry.get('max', 1))
                        if qty > 0:
                            inv_comp.add(entry['item'], qty)
                # Persistir inventario generado
                active_monsters[iid] = {
                    'template_id': template_id,
                    'slots': inv_comp.serialize().get('slots'),
                    'schema_version': self.schema_version
                }
                self.dirty_monsters = True
            # Asignar componente e inicializar marcado
            world.components['InventoryComponent'][eid] = inv_comp
            self.initialized.add(eid)

        # Remove entries for monsters no longer present
        current_npc_keys = set(inst.instance_id for eid, inst in instance_store.items() if eid in npc_tag_store)
        for key in list(active_monsters.keys()):
            if key not in current_npc_keys:
                active_monsters.pop(key)
                self.dirty_monsters = True
        # Guardar archivos activos solo si hay cambios
        if self.dirty_monsters:
            with open(self.active_monster_path, 'w') as f:
                json.dump(self.active_monsters, f, indent=2)
        if self.dirty_players:
            with open(self.active_player_path, 'w') as f:
                json.dump(self.active_players, f, indent=2)
