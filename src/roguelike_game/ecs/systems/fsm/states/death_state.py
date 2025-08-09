from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
from roguelike_game.ecs.components.rendering.grayscale_component import GrayscaleComponent
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.config.players_config import PLAYER_STATS
import time
import json
import os

import logging
logger = logging.getLogger(__name__)

class DeathState(State):
    """
    Estado Death: gestiona temporizador y eliminación de entidad muerta.
    """
    def enter(self, entity):
        """Registra temporizador."""
        world = entity.world
        eid = entity.id
        logger.debug(f"[DeathState.enter] eid={eid}, is_player={eid in world.components.get('PlayerTagComponent', {})}")
        # Iniciar temporizador según configuración de players.json
        pt = world.components.get('PlayerTagComponent', {}).get(eid)
        cls_name = getattr(pt, 'class_name', None)
        if cls_name in PLAYER_STATS:
            duration = PLAYER_STATS[cls_name].get('basic_death_timer_duration', 60.0)
        else:
            duration = 60.0
        world.components['DeathTimer'][eid] = DeathTimer(time.time(), duration)
        # Cambiar el sprite al de muerte para ocultar el sprite anterior
        sprite = world.components.get('Sprite', {}).get(eid)
        if sprite and hasattr(sprite, 'death_image'):
            sprite.image = sprite.death_image
            # Deshabilitar animación para no sobreescribir el sprite de muerte
            world.components.get('Animator', {}).pop(eid, None)
            world.components.get('AnimationTimer', {}).pop(eid, None)


    def execute(self, entity, dt):
        """Espera a que expire el temporizador antes de eliminar la entidad."""
        world = entity.world
        nid = entity.id
        dt_cmp = world.components['DeathTimer'][nid]
        now = time.time()
        elapsed = now - dt_cmp.start_time
        duration = dt_cmp.duration
        comps = world.components
        # Ejecutar acciones tras expiración del temporizador
        if elapsed >= duration:
            if nid in comps.get('PlayerTagComponent', {}):
                if nid not in comps.get('GrayscaleComponent', {}):
                    comps['GrayscaleComponent'][nid] = GrayscaleComponent()
            else:
                world.remove_entity(nid)
                # Limpiar inventario activo para este monstruo
                try:
                    with open(os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'), 'r') as f:
                        inv = json.load(f)
                except (json.JSONDecodeError, FileNotFoundError):
                    inv = {}
                inv.pop(str(nid), None)
                with open(os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_monsters.json'), 'w') as f:
                    json.dump(inv, f, indent=2)
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
                    # Revivir: quitar grayscale, timer y restaurar vida
                    comps['GrayscaleComponent'].pop(nid, None)
                    comps['DeathTimer'].pop(nid, None)
                    hp = world.components.get('Health', {}).get(nid)
                    if hp:
                        hp.current_hp = hp.max_hp
                    # Cambiar FSM a IdleState
                    npc_state = comps.get('NPCState', {}).get(nid)
                    if npc_state:
                        from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
                        npc_state.fsm.change_state(IdleState(), entity)
                    logger.debug(f"[DeathState.execute] eid={nid} revived in lobby")
        # Debug logs: solo una vez cada segundo
        if now - dt_cmp.last_log_time >= 1.0:
            if elapsed >= duration:
                logger.debug(f"[DeathState.execute] Timer expired for eid={nid}")
                if nid in comps.get('PlayerTagComponent', {}):
                    logger.debug(f"[DeathState.execute] eid={nid} is Player -> grayscaling once")
                else:
                    logger.debug(f"[DeathState.execute] eid={nid} removed from world")
            else:
                logger.debug(f"[DeathState.execute] eid={nid}, elapsed={elapsed:.2f}/{duration} - waiting")
            dt_cmp.last_log_time = now


    def exit(self, entity):
        """Limpia si fuera necesario."""
        logger.debug(f"[DeathState.exit] eid={entity.id}")
        pass