from __future__ import annotations

import random
import uuid
from typing import Any

from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.core.identity import Faction
from .inventory_io import write_json
from roguelike_game.ecs.systems.vendors.services import EconomyService


def run_inventory_init_update(system: Any, world: Any, *args: Any) -> None:
    comps = world.components
    player_tag_store = comps.get('PlayerTagComponent', {})
    npc_tag_store = comps.get('NPCTagComponent', {})
    instance_store = comps.get('MonsterInstanceComponent', {})

    # Cargar datos activos
    # Cargar datos activos
    # Use in-memory active_monsters
    active_monsters = system.active_monsters
    # Reset dirty flag for monsters
    system.dirty_monsters = False
    # Use in-memory active_players
    active_players = system.active_players
    # Reset dirty flag for players
    system.dirty_players = False
    # Use in-memory active_neutrals
    active_neutrals = system.active_neutrals
    # Reset dirty flag for neutrals
    system.dirty_neutrals = False

    # Inicializar jugadores
    for eid in list(player_tag_store.keys()):
        if eid in system.initialized:
            continue
        key = str(eid)
        # Normalizar player_id en active_players siempre que exista la entrada,
        # incluso si ya hay InventoryComponent (no sobrescribimos el componente, solo saneamos el store)
        if key in active_players:
            pdata = active_players[key]
            pid = pdata.get('player_id')
            try:
                uuid.UUID(str(pid)) if pid else (_ for _ in ()).throw(ValueError())
            except Exception:
                pid = str(uuid.uuid4())
                pdata['player_id'] = pid
                system.dirty_players = True
        # Si ya existen componentes (cargados desde un save), NO sobrescribirlos
        existing_inv = world.components.get('InventoryComponent', {}).get(eid)
        existing_xp  = world.components.get('ExperienceComponent', {}).get(eid)

        # Inventario: solo crear/asignar si no existe ya
        if existing_inv is None:
            # Cargar inventario persistido de activos si existe
            if key in active_players:
                pdata = active_players[key]
                # Usar player_id ya normalizado arriba
                pid = pdata.get('player_id')
                inv_comp = InventoryComponent(
                    capacity=system.player_template.get('capacity', 20),
                    player_id=pid
                )
                for slot in pdata.get('slots', []):
                    if slot:
                        inv_comp.add(slot['item'], slot['quantity'])
            else:
                # Crear InventoryComponent con plantilla por defecto
                capacity = system.player_template.get('capacity', 20)
                # Normalizar/generar player_id si plantilla no lo define
                player_id = system.player_template.get('player_id')
                try:
                    uuid.UUID(str(player_id)) if player_id else (_ for _ in ()).throw(ValueError())
                except Exception:
                    player_id = str(uuid.uuid4())
                inv_comp = InventoryComponent(capacity=capacity, player_id=player_id)
                for slot in system.player_template.get('slots', []):
                    if slot:
                        inv_comp.add(slot['item'], slot['quantity'])
                # Persistir inicial
                active_players[key] = {
                    'player_id': player_id,
                    'slots': inv_comp.serialize().get('slots'),
                    'schema_version': system.schema_version
                }
                system.dirty_players = True
            world.components['InventoryComponent'][eid] = inv_comp

        # Experiencia: solo crear por defecto si no existe ya
        if existing_xp is None:
            # ExperienceComponent is imported in system file; we access it through world injection
            from roguelike_game.ecs.components.experience_component import ExperienceComponent
            world.components['ExperienceComponent'][eid] = ExperienceComponent()
        system.initialized.add(eid)

    # Inicializar NPCs (hostiles y neutrales)
    for eid in list(npc_tag_store.keys()):
        inst = instance_store.get(eid)
        if not inst:
            continue
        iid = inst.instance_id
        # Saltar si ya inicializado
        if iid in system.initialized:
            continue
        if eid in system.initialized:
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
            template = (system.neutral_templates.get(template_key) if is_neutral else system.monster_templates.get(template_key))
        if not template:
            # Si no hay plantilla (p.ej., neutrales sin entry), continuar pero permitir semillas para vendors
            template = {}
        active_store = active_neutrals if is_neutral else active_monsters

        # Preferir snapshot desde el save actual expuesto en ECSWorld.components
        # a través de la clave 'NPCInventorySnapshot' (inyectada post-load).
        try:
            world_snapshots = comps.get('NPCInventorySnapshot', {}) or {}
        except Exception:
            world_snapshots = {}
        template_id = template.get('template_id')
        if iid in world_snapshots:
            # Cargar inventario desde snapshot del save
            saved = world_snapshots.get(iid, {}) or {}
            try:
                file_tid = saved.get('template_id') or template_id
            except Exception:
                file_tid = template_id
            inv_comp = InventoryComponent(player_id=file_tid)
            for slot in saved.get('slots', []) or []:
                if slot:
                    try:
                        qty = int(slot.get('quantity', 0))
                    except Exception:
                        qty = slot.get('quantity', 0)
                    inv_comp.add(slot.get('item'), qty)
            # Persistir inventario restaurado en activos para coherencia de runtime
            active_store[iid] = {
                'template_id': file_tid,
                'slots': inv_comp.serialize().get('slots'),
                'schema_version': system.schema_version
            }
            # Si tras cargar snapshot no hay stock vendible (según economía), intentar resembrar desde semilla
            try:
                eco = getattr(system, '_economy_service_for_seed', None)
                if eco is None:
                    eco = EconomyService()
                    setattr(system, '_economy_service_for_seed', eco)
                allowed_ids = None
                try:
                    allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
                except Exception:
                    allowed_ids = None
                if isinstance(allowed_ids, set):
                    has_stock = any(
                        st is not None
                        and (iid := str(getattr(st, 'item_id', '')).lower()) != 'gold'
                        and iid in allowed_ids
                        for st in getattr(inv_comp, 'slots', []) or []
                    )
                else:
                    has_stock = any(
                        st is not None and str(getattr(st, 'item_id', '')).lower() != 'gold'
                        for st in getattr(inv_comp, 'slots', []) or []
                    )
            except Exception:
                has_stock = False
            if not has_stock:
                try:
                    identity_key = (template_key or '').lower()
                    is_vendor = (eid in comps.get('VendorComponent', {})) or ('vendor' in identity_key)
                    if is_vendor and identity_key:
                        eco = getattr(system, '_economy_service_for_seed', None)
                        if eco is None:
                            eco = EconomyService()
                            setattr(system, '_economy_service_for_seed', eco)
                        allowed_ids = None
                        try:
                            allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
                        except Exception:
                            allowed_ids = None
                        inv_from_seed = system.vendor_support.try_build_inventory_from_seed(identity_key, template_id, allowed_ids=allowed_ids)
                        if inv_from_seed is not None:
                            inv_comp = inv_from_seed
                            active_store[iid] = {
                                'template_id': inv_comp.player_id,
                                'slots': inv_comp.serialize().get('slots'),
                                'schema_version': system.schema_version
                            }
                except Exception:
                    pass
            if is_neutral:
                system.dirty_neutrals = True
            else:
                system.dirty_monsters = True
        elif iid in active_store:
            # Cargar inventario activo existente
            saved = active_store.get(iid, {})
            inv_comp = InventoryComponent(player_id=saved.get('template_id', template_id))
            for slot in saved.get('slots', []):
                if slot:
                    inv_comp.add(slot['item'], slot.get('quantity', 0))
            # Si tras cargar activos no hay stock vendible (según economía), intentar resembrar desde semilla
            try:
                eco = getattr(system, '_economy_service_for_seed', None)
                if eco is None:
                    eco = EconomyService()
                    setattr(system, '_economy_service_for_seed', eco)
                allowed_ids = None
                try:
                    allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
                except Exception:
                    allowed_ids = None
                if isinstance(allowed_ids, set):
                    has_stock = any(
                        st is not None
                        and (iid := str(getattr(st, 'item_id', '')).lower()) != 'gold'
                        and iid in allowed_ids
                        for st in getattr(inv_comp, 'slots', []) or []
                    )
                else:
                    has_stock = any(
                        st is not None and str(getattr(st, 'item_id', '')).lower() != 'gold'
                        for st in getattr(inv_comp, 'slots', []) or []
                    )
            except Exception:
                has_stock = False
            if not has_stock:
                try:
                    identity_key = (template_key or '').lower()
                    is_vendor = (eid in comps.get('VendorComponent', {})) or ('vendor' in identity_key)
                    if is_vendor and identity_key:
                        eco = getattr(system, '_economy_service_for_seed', None)
                        if eco is None:
                            eco = EconomyService()
                            setattr(system, '_economy_service_for_seed', eco)
                        allowed_ids = None
                        try:
                            allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
                        except Exception:
                            allowed_ids = None
                        inv_from_seed = system.vendor_support.try_build_inventory_from_seed(identity_key, template_id, allowed_ids=allowed_ids)
                        if inv_from_seed is not None:
                            inv_comp = inv_from_seed
                            active_store[iid] = {
                                'template_id': inv_comp.player_id,
                                'slots': inv_comp.serialize().get('slots'),
                                'schema_version': system.schema_version
                            }
                            if is_neutral:
                                system.dirty_neutrals = True
                            else:
                                system.dirty_monsters = True
                except Exception:
                    pass
        else:
            # Si no hay activo persistido, intentar cargar semilla específica de vendor
            loaded_from_seed = False
            try:
                identity_key = (template_key or '').lower()
                is_vendor = (eid in comps.get('VendorComponent', {})) or ('vendor' in identity_key)
                if is_vendor and identity_key:
                    # Resolver ids permitidos por tipo desde SQLite para este vendor
                    eco = getattr(system, '_economy_service_for_seed', None)
                    if eco is None:
                        eco = EconomyService()
                        setattr(system, '_economy_service_for_seed', eco)
                    allowed_ids = None
                    try:
                        allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
                    except Exception:
                        allowed_ids = None
                    # Cargar registry para obtener semilla específica y grupo
                    inv_from_seed = system.vendor_support.try_build_inventory_from_seed(identity_key, template_id, allowed_ids=allowed_ids)
                    if inv_from_seed is not None:
                        inv_comp = inv_from_seed
                        # Persistir inventario inicializado en activos para esta instancia
                        active_store[iid] = {
                            'template_id': inv_comp.player_id,
                            'slots': inv_comp.serialize().get('slots'),
                            'schema_version': system.schema_version
                        }
                        if is_neutral:
                            system.dirty_neutrals = True
                        else:
                            system.dirty_monsters = True
                        loaded_from_seed = True
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
                    'schema_version': system.schema_version
                }
                if is_neutral:
                    system.dirty_neutrals = True
                else:
                    system.dirty_monsters = True
        # Asignar componente e inicializar marcado
        world.components['InventoryComponent'][eid] = inv_comp
        # Si el NPC tiene chat, asegurar capacidad de comercio básica (oro + algo de stock)
        try:
            has_chat = eid in comps.get('ChatComponent', {})
        except Exception:
            has_chat = False
        if has_chat:
            # Usar los ids permitidos por tipo también para sembrado adicional
            allowed_ids = None
            try:
                eco = getattr(system, '_economy_service_for_seed', None)
                if eco is None:
                    eco = EconomyService()
                    setattr(system, '_economy_service_for_seed', eco)
                allowed_ids = eco.get_allowed_item_ids_by_type(world, eid)
            except Exception:
                allowed_ids = None
            # Si tenemos lista de ids permitidos (type=food), asegurar capacidad y restock completo (100 c/u)
            try:
                if isinstance(allowed_ids, set) and allowed_ids:
                    # 1) Asegurar capacidad suficiente para una pila por cada item permitido + margen
                    try:
                        current_cap = int(getattr(inv_comp, 'capacity', 20) or 20)
                    except Exception:
                        current_cap = 20
                    target_cap = max(current_cap, len(allowed_ids) + 10)
                    if target_cap > current_cap:
                        # Reconstruir inventario con mayor capacidad y copiar contenido
                        new_inv = InventoryComponent(capacity=target_cap, player_id=getattr(inv_comp, 'player_id', None))
                        try:
                            for st in getattr(inv_comp, 'slots', []) or []:
                                if st:
                                    new_inv.add(getattr(st, 'item_id', ''), int(getattr(st, 'quantity', 0) or 0))
                            inv_comp = new_inv
                            world.components['InventoryComponent'][eid] = inv_comp
                        except Exception:
                            # si falla la copia, mantenemos el original
                            pass
                    # 2) Restock: garantizar al menos 100 unidades de cada item permitido
                    for aid in sorted(allowed_ids):
                        try:
                            # calcular cantidad actual
                            current_qty = 0
                            for st in getattr(inv_comp, 'slots', []) or []:
                                if st and str(getattr(st, 'item_id', '')).lower() == str(aid).lower():
                                    current_qty += int(getattr(st, 'quantity', 0) or 0)
                            if current_qty < 100:
                                inv_comp.add(str(aid), 100 - current_qty)
                        except Exception:
                            continue
                    # Persistir después del restock completo
                    active_store[iid] = {
                        'template_id': inv_comp.player_id,
                        'slots': inv_comp.serialize().get('slots'),
                        'schema_version': system.schema_version
                    }
                    if is_neutral:
                        system.dirty_neutrals = True
                    else:
                        system.dirty_monsters = True
            except Exception:
                pass
            system._maybe_seed_trader(eid, inv_comp, is_neutral=is_neutral, active_store=active_store, iid=iid, allowed_ids=allowed_ids)
        system.initialized.add(eid)

    # Remove entries for monsters/neutrals no longer present
    current_npc_keys = set(inst.instance_id for eid, inst in instance_store.items() if eid in npc_tag_store)
    for key in list(active_monsters.keys()):
        if key not in current_npc_keys:
            active_monsters.pop(key)
            system.dirty_monsters = True
    for key in list(active_neutrals.keys()):
        if key not in current_npc_keys:
            active_neutrals.pop(key)
            system.dirty_neutrals = True
    # Guardar archivos activos solo si hay cambios
    if system.dirty_monsters:
        write_json(system.active_monster_path, system.active_monsters)
    if system.dirty_players:
        write_json(system.active_player_path, system.active_players)
    if system.dirty_neutrals:
        write_json(system.active_neutral_path, system.active_neutrals)
