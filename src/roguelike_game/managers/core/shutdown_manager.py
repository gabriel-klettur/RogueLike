from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE
from datetime import datetime
from pathlib import Path
from roguelike_game.utils.inventory_sync import write_active_for_player
from roguelike_game.utils.inventory_registry import publish_inventory
from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_game.ecs.components.spawner.spawner_child import SpawnerChild

import logging
logger = logging.getLogger(__name__)

class ShutdownManager:
    """
    Se encarga de todo lo necesario antes de cerrar el juego:
     - Guardar posición del jugador en el mapa actual.
     - Actualizar WorldManager (maps, current_level, etc.).
     - Serializar y guardar el mundo en disco.
    """
    def __init__(self, game):
        self.game = game

    def shutdown(self):
        g = self.game
        try:
            # 1) Obtener la entidad del jugador
            eid = g.ecs.ecs_world.player_entity

            pos = g.ecs.ecs_world.components["Position"][eid]

            # 2) Calcular coordenadas de tile usando el centro del collider 'feet'
            w, h = RENDERED_SPRITE_SIZE
            fh = h // 4
            half_fh = fh // 2
            feet_cx = pos.x + w//2
            feet_cy = pos.y + (h - half_fh)

            tx = int(feet_cx // TILE_SIZE)
            ty = int(feet_cy // TILE_SIZE)

            # 3) Hacer spawn del jugador en el mapa (para que guarde la nueva posición)
            g.map.spawn_player((tx, ty))

            # 4) Actulizar WorldManager
            g.world.maps[g.map.name] = g.map
            g.world.current_level     = g.map.name

            # 4b) Persistir inventario del jugador si existe
            try:
                inv = g.ecs.ecs_world.components.get("InventoryComponent", {}).get(eid)
                if inv is not None and hasattr(inv, "serialize"):
                    g.world.player_inventory = inv.serialize()
                    # Sincronizar también el perfil activo
                    try:
                        write_active_for_player(eid, g.world.player_inventory)
                    except Exception:
                        pass
                    # Publicar snapshot en registro versionado (opcional)
                    try:
                        publish_inventory(g.world.player_inventory)
                    except Exception:
                        pass
            except Exception:
                pass

            # 4c) Preparar metadatos del guardado: nombre, timestamps, xp, nivel, items
            try:
                # Nombre de guardado: si no existe, usar nombre basado en archivo
                meta = dict(g.world.save_metadata or {})
                # created_at: mantener si ya existe, si no, setear ahora
                created = meta.get("created_at") or datetime.now().isoformat(timespec='seconds')
                # last_played: siempre actualizar
                last_played = datetime.now().isoformat(timespec='seconds')
                # name: mantener si existe, si no, derivar de nombre de archivo del slot si existe
                slot_path = g.world.current_save_path
                default_name = Path(slot_path).stem if slot_path else "partida"
                meta_name = meta.get("name") or default_name

                # Extraer xp/nivel del jugador
                xp_val = None
                level_val = None
                try:
                    xp_comp = g.ecs.ecs_world.components.get("ExperienceComponent", {}).get(eid)
                    if xp_comp is not None:
                        xp_val = getattr(xp_comp, 'xp', None)
                        level_val = getattr(xp_comp, 'level', None)
                except Exception:
                    pass

                # Resumen de items: contar stacks y listar primeros 5 ids
                items_summary = {}
                try:
                    if inv is not None:
                        slots = getattr(inv, 'slots', [])
                        stacks = [s for s in slots if s]
                        items_summary = {
                            "stacks": len(stacks),
                            "top_items": [getattr(s, 'item_id', None) for s in stacks[:5]]
                        }
                except Exception:
                    pass

                meta.update({
                    "name": meta_name,
                    "created_at": created,
                    "last_played": last_played,
                    "player": {
                        "xp": xp_val,
                        "level": level_val,
                    },
                    "items_summary": items_summary,
                })
                g.world.save_metadata = meta
            except Exception:
                pass

            # 4d) Persistir estado de NPCs (vida, posición en tiles y su inventario)
            try:
                ecs_world = g.ecs.ecs_world
                comps = ecs_world.components
                npc_tags = comps.get("NPCTagComponent", {}) or {}
                inst_store = comps.get("MonsterInstanceComponent", {}) or {}
                health_store = comps.get("Health", {}) or {}
                death_store = comps.get("DeathTimer", {}) or {}
                inv_store = comps.get("InventoryComponent", {}) or {}
                archetype_store = comps.get("MonsterArchetype", {}) or {}
                identity_store = comps.get("Identity", {}) or {}
                sp_children = comps.get("SpawnerChild", {}) or {}

                # Asegurar estructuras en WorldManager
                if getattr(g.world, 'npc_memory', None) is None:
                    g.world.npc_memory = {}
                if getattr(g.world, 'npc_inventories', None) is None:
                    g.world.npc_inventories = {}

                # Nivel actual
                current_level = getattr(g.world, 'current_level', None) or getattr(g.map, 'name', None)

                # Limpiar memoria previa del nivel actual para evitar acumulación y duplicados
                try:
                    prev_mem = len(getattr(g.world, 'npc_memory', {}) or {})
                    prev_inv = len(getattr(g.world, 'npc_inventories', {}) or {})
                    if isinstance(g.world.npc_memory, dict):
                        g.world.npc_memory = {
                            k: v for k, v in g.world.npc_memory.items()
                            if (v or {}).get('level') != current_level
                        }
                    if isinstance(g.world.npc_inventories, dict):
                        g.world.npc_inventories = {
                            k: v for k, v in g.world.npc_inventories.items()
                            if (g.world.npc_memory.get(k, {}) or {}).get('level') != current_level
                        }
                    try:
                        logger.debug(
                            "[Save] Cleared previous npc_memory entries for level=%s (prev_npcs=%s prev_inventories=%s)",
                            current_level, prev_mem, prev_inv
                        )
                    except Exception:
                        pass
                except Exception:
                    pass

                skipped_children = 0
                persisted_npcs = 0
                for neid in list(npc_tags.keys()):
                    # Omitir NPCs hijos de spawner (ephemerales). Su reaparición la gestiona el spawner.
                    if neid in sp_children:
                        skipped_children += 1
                        continue
                    inst = inst_store.get(neid)
                    if not inst:
                        continue
                    instance_id = getattr(inst, 'instance_id', None)
                    if not instance_id:
                        continue

                    # Posición en tiles usando pies del sprite
                    try:
                        tx, ty = compute_foot_tile(ecs_world, neid, TILE_SIZE) or (None, None)
                    except Exception:
                        tx, ty = (None, None)

                    # Vida actual y estado de muerte
                    hp_cmp = health_store.get(neid)
                    current_hp = getattr(hp_cmp, 'current_hp', None) if hp_cmp is not None else None
                    dead_flag = False
                    try:
                        dead_flag = (current_hp is not None and current_hp <= 0) or (neid in death_store)
                    except Exception:
                        pass

                    # Determinar prototipo/arquetipo
                    proto = None
                    try:
                        at = archetype_store.get(neid)
                        if at is not None:
                            proto = getattr(at, 'type', None)
                    except Exception:
                        proto = None
                    if not proto:
                        try:
                            ident = identity_store.get(neid)
                            if ident is not None:
                                proto = str(getattr(ident, 'name', None) or '')
                        except Exception:
                            pass

                    # Guardar memoria mínima del NPC
                    g.world.npc_memory[str(instance_id)] = {
                        "level": current_level,
                        "tile": [int(tx), int(ty)] if tx is not None and ty is not None else None,
                        "hp": int(current_hp) if isinstance(current_hp, (int, float)) else current_hp,
                        "dead": bool(dead_flag),
                        "prototype": proto,
                    }
                    persisted_npcs += 1

                    # Snapshot de inventario (si tiene)
                    inv_cmp = inv_store.get(neid)
                    if inv_cmp is not None and hasattr(inv_cmp, 'serialize'):
                        try:
                            snap = inv_cmp.serialize() or {}
                            # Normalizar slots: cantidades como int
                            slots = []
                            for s in snap.get('slots', []) or []:
                                if s:
                                    qty = s.get('quantity', 0)
                                    try:
                                        qty = int(qty)
                                    except Exception:
                                        pass
                                    slots.append({"item": s.get('item'), "quantity": qty})
                            norm = {k: v for k, v in snap.items() if k != 'slots'}
                            norm['slots'] = slots
                            g.world.npc_inventories[str(instance_id)] = norm
                        except Exception:
                            pass
            except Exception:
                pass

            # 4d summary logging
            try:
                logger.info(
                    "[Save] NPC persistence summary: level=%s skipped_spawner_children=%s persisted_npcs=%s total_memory=%s",
                    current_level, skipped_children, persisted_npcs, len(getattr(g.world, 'npc_memory', {}) or {})
                )
            except Exception:
                pass

            # 4e) Sincronizar npc_states locales de cada mapa cargado con npc_memory global
            try:
                npc_mem = getattr(g.world, 'npc_memory', {}) or {}
                synced_levels = 0
                for lvl_name, mgr in (getattr(g.world, 'maps', {}) or {}).items():
                    try:
                        ls = getattr(mgr, '_local_state', None)
                        if not isinstance(ls, dict):
                            continue
                        filtered = {iid: st for iid, st in npc_mem.items() if (st or {}).get('level') == lvl_name}
                        prev_cnt = len(ls.get('npc_states', {}) or {})
                        ls['npc_states'] = dict(filtered)
                        synced_levels += 1
                        try:
                            logger.info(
                                "[Save] Synced map npc_states for level=%s: prev=%s now=%s",
                                lvl_name, prev_cnt, len(filtered)
                            )
                        except Exception:
                            pass
                    except Exception:
                        continue
                if synced_levels == 0:
                    try:
                        # Si no hay mapas en memoria, al menos sincronizar el mapa actual si existe referencia directa
                        mgr = getattr(g, 'map', None)
                        if mgr is not None:
                            lvl_name = getattr(mgr, 'name', None)
                            ls = getattr(mgr, '_local_state', None)
                            if isinstance(ls, dict) and lvl_name:
                                filtered = {iid: st for iid, st in npc_mem.items() if (st or {}).get('level') == lvl_name}
                                prev_cnt = len(ls.get('npc_states', {}) or {})
                                ls['npc_states'] = dict(filtered)
                                logger.info(
                                    "[Save] Synced map npc_states for current level=%s: prev=%s now=%s",
                                    lvl_name, prev_cnt, len(filtered)
                                )
                    except Exception:
                        pass
            except Exception:
                pass

            # 5) Log de resumen previo al guardado
            try:
                level_name = getattr(g.world, 'current_level', None) or getattr(g.map, 'name', None)
                player_tile = (tx, ty)
                npc_cnt = len(getattr(g.world, 'npc_memory', {}) or {})
                inv_cnt = len(getattr(g.world, 'npc_inventories', {}) or {})
                slot_hint = getattr(g.world, 'current_save_path', None)
                logger.info(
                    f"[Save] Preparando guardado: nivel={level_name}, player_tile={player_tile}, npcs={npc_cnt}, npc_inventarios={inv_cnt}, slot={slot_hint}"
                )
            except Exception:
                pass

            # 6) Salvar el mundo en disco
            g.world.save_world()

        except Exception as exc:
            logger.warning(f"No se pudo guardar al cerrar: {exc}")