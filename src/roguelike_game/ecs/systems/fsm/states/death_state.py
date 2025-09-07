from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.velocity import Velocity

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
import json
import os

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
        # NPCs: eliminar inmediatamente
        if eid not in world.components.get('PlayerTagComponent', {}):
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