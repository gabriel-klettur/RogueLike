from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.velocity import Velocity

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
import json
import os
from roguelike_game.ecs.utils.position_utils import compute_foot_tile

import logging
logger = logging.getLogger(__name__)

class DeathState(State):
    """
    Estado Death: estado final.
    - Para NPCs: elimina inmediatamente la entidad y limpia inventario.
    - Para Player: aplica escala de grises y permite lógica de revive en lobby.
    El temporizador de desaparición se gestiona en UnconsciousState.
    """
    def enter(self, entity):
        world = entity.world
        eid = entity.id
        logger.debug(f"[DeathState.enter] eid={eid}, is_player={eid in world.components.get('PlayerTagComponent', {})}")
        # Asegurar que no quede parpadeo activo: eliminar FlashComponent si existe
        world.components.get('FlashComponent', {}).pop(eid, None)
        # Anular cualquier movimiento residual
        vel_map = world.components.get('Velocity', {})
        if eid in vel_map:
            try:
                vel_map[eid].vx = 0
                vel_map[eid].vy = 0
            except Exception:
                world.components.setdefault('Velocity', {})[eid] = Velocity(0, 0)
        else:
            world.components.setdefault('Velocity', {})[eid] = Velocity(0, 0)
        # Deshabilitar animación para no sobreescribir
        world.components.get('Animator', {}).pop(eid, None)
        world.components.get('AnimationTimer', {}).pop(eid, None)
        # NPCs: persist quick-death flag and remove immediately
        if eid not in world.components.get('PlayerTagComponent', {}):
            # Registrar muerte en el estado local del mapa para evitar respawns no deseados
            try:
                inst_cmp = world.components.get('MonsterInstanceComponent', {}).get(eid)
                instance_id = getattr(inst_cmp, 'instance_id', None) if inst_cmp is not None else None
                if instance_id:
                    tile = compute_foot_tile(world, eid, TILE_SIZE)
                    tx, ty = (int(tile[0]), int(tile[1])) if tile else (None, None)
                    level_name = getattr(world.map_manager, 'name', None)
                    # Determinar prototipo
                    proto = None
                    at = world.components.get('MonsterArchetype', {}).get(eid)
                    if at is not None:
                        try:
                            proto = getattr(at, 'type', None)
                        except Exception:
                            proto = None
                    if not proto:
                        ident = world.components.get('Identity', {}).get(eid)
                        if ident is not None:
                            try:
                                proto = str(getattr(ident, 'name', None) or '')
                            except Exception:
                                proto = None
                    st = {
                        'level': level_name,
                        'tile': [int(tx), int(ty)] if tx is not None and ty is not None else None,
                        'hp': 0,
                        'dead': True,
                        'prototype': proto,
                    }
                    try:
                        m = getattr(world, 'map_manager', None)
                        if m is not None:
                            ls = getattr(m, '_local_state', None)
                            if isinstance(ls, dict):
                                npc_states = ls.setdefault('npc_states', {})
                                npc_states[str(instance_id)] = st
                                try:
                                    logger.info(
                                        "[DeathState] Marked NPC instance_id=%s dead at level=%s tile=%s",
                                        instance_id, level_name, st.get('tile')
                                    )
                                except Exception:
                                    pass
                    except Exception:
                        pass
            except Exception:
                pass
            world.remove_entity(eid)
            # Limpiar inventario activo para este monstruo
            try:
                with open(os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'), 'r') as f:
                    inv = json.load(f)
            except (json.JSONDecodeError, FileNotFoundError):
                inv = {}
            inv.pop(str(eid), None)
            with open(os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'), 'w') as f:
                json.dump(inv, f, indent=2)
            return
        # Player: aplicar grayscale si no presente
        comps = world.components
        if eid not in comps.get('GrayscaleComponent', {}):
            comps.setdefault('GrayscaleComponent', {})[eid] = GrayscaleComponent()
        # Asegurar ZLayer adecuado del jugador (sobre cadáveres y objetos bajos)
        comps.setdefault('ZLayer', {})[eid] = ZLayer(Z_LAYERS.get('player', 4))

    def execute(self, entity, dt):
        """Lógica de revive del jugador en lobby 3x3."""
        world = entity.world
        nid = entity.id
        comps = world.components
        # Lógica de resurrección: si está en lobby 3x3 y en gris, revivir
        if nid in comps.get('PlayerTagComponent', {}) and nid in comps.get('GrayscaleComponent', {}):
            pos = world.components.get('Position', {}).get(nid)
            if pos:
                tx = int(pos.x // TILE_SIZE)
                ty = int(pos.y // TILE_SIZE)
                lob_x, lob_y = world.map_manager.lobby_offset
                cw = global_map_settings.zone_width
                ch = global_map_settings.zone_height
                center_tx = lob_x + cw // 2
                center_ty = lob_y + ch // 2
                if center_tx-1 <= tx <= center_tx+1 and center_ty-1 <= ty <= center_ty+1:
                    # Revivir: quitar grayscale y restaurar vida
                    comps['GrayscaleComponent'].pop(nid, None)
                    hp = world.components.get('Health', {}).get(nid)
                    if hp:
                        hp.current_hp = hp.max_hp
                    # Cambiar FSM a IdleState
                    npc_state = comps.get('NPCState', {}).get(nid)
                    if npc_state:
                        from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
                        npc_state.fsm.change_state(IdleState(), entity)
                    logger.debug(f"[DeathState.execute] eid={nid} revived in lobby")

    def exit(self, entity):
        logger.debug(f"[DeathState.exit] eid={entity.id}")
        pass