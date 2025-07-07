import os
import json
import random

from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent
from roguelike_game.ecs.components.inventory_component import InventoryComponent


class InventoryInitSystem:
    """
    Sistema ECS que inicializa inventarios para Player y NPCs desde plantillas por defecto
    y persiste el estado inicial en archivos JSON activos.
    """
    def __init__(self, perf_log=None,
                 default_monster_path: str = 'data/defaults/inventory_monsters.json',
                 active_monster_path: str = 'data/inventory_monsters.json',
                 default_player_path: str = 'data/defaults/inventory_player.json',
                 active_player_path: str = 'data/inventory_player.json',
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

    def update(self, world, *args):
        comps = world.components
        player_tag_store = comps.get('PlayerTagComponent', {})
        npc_tag_store = comps.get('NPCTagComponent', {})

        # Cargar datos activos
        # Cargar datos activos
        try:
            with open(self.active_monster_path, 'r') as f:
                active_monsters = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError):
            active_monsters = {}
            os.makedirs(os.path.dirname(self.active_monster_path), exist_ok=True)
            with open(self.active_monster_path, 'w') as f:
                json.dump(active_monsters, f, indent=2)
        try:
            with open(self.active_player_path, 'r') as f:
                active_players = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError):
            active_players = {}
            os.makedirs(os.path.dirname(self.active_player_path), exist_ok=True)
            with open(self.active_player_path, 'w') as f:
                json.dump(active_players, f, indent=2)

        # Inicializar jugadores
        for eid in list(player_tag_store.keys()):
            if eid in self.initialized:
                continue
            # Crear InventoryComponent
            capacity = self.player_template.get('capacity', 20)
            player_id = self.player_template.get('player_id')
            inv_comp = InventoryComponent(capacity=capacity, player_id=player_id)
            # Poblar slots
            for slot in self.player_template.get('slots', []):
                if slot:
                    inv_comp.add(slot['item'], slot['quantity'])
            world.components['InventoryComponent'][eid] = inv_comp
            # Persistir
            active_players[str(eid)] = {
                'player_id': player_id,
                'slots': inv_comp.serialize().get('slots'),
                'schema_version': self.schema_version
            }
            self.initialized.add(eid)

        # Inicializar NPCs
        for eid in list(npc_tag_store.keys()):
            if eid in self.initialized:
                continue
            # Determinar plantilla a partir de Identity.name
            identity = comps.get('Identity', {}).get(eid)
            template_key = identity.name.lower() if identity else None
            template = self.monster_templates.get(template_key)
            if not template:
                continue
            template_id = template.get('template_id')
            inv_comp = InventoryComponent(player_id=template_id)
            # Generar ítems según rangos y probabilidades
            for entry in template.get('inventory', []):
                if random.random() <= entry.get('chance', 1.0):
                    qty = random.randint(entry.get('min', 1), entry.get('max', 1))
                    if qty > 0:
                        inv_comp.add(entry['item'], qty)
            world.components['InventoryComponent'][eid] = inv_comp
            # Persistir
            active_monsters[str(eid)] = {
                'template_id': template_id,
                'slots': inv_comp.serialize().get('slots'),
                'schema_version': self.schema_version
            }
            self.initialized.add(eid)

        # Guardar archivos activos
        with open(self.active_monster_path, 'w') as f:
            json.dump(active_monsters, f, indent=2)
        with open(self.active_player_path, 'w') as f:
            json.dump(active_players, f, indent=2)
