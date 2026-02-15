"""
Module: spawn_system.py
Handles conversion of SpawnRequest components into actual game entities
using the entity factory.
"""
from roguelike_game.factories.registry import get_factory
from roguelike_game.ecs.components.spawn.spawn_stabilizer import SpawnStabilizer
from roguelike_game.ecs.components.core.timed_despawn import TimedDespawn

from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.ai.defend_area import DefendArea
from roguelike_game.ecs.components.fsm.patrol_route import PatrolRoute
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.monster.behaviour_loader import build_patrol_route
from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
from roguelike_game.ecs.components.spawner.spawner_child import SpawnerChild
import logging

logger = logging.getLogger(__name__)

class SpawnSystem:
    """
    Sistema que procesa componentes SpawnRequest y genera NPCs en el mundo.

    Usa un *spawn budget* para limitar cuántos NPCs se crean por frame,
    distribuyendo el costo de creación a lo largo de varios frames y evitando
    spikes de latencia.
    """

    # Máximo de SpawnRequests procesados por frame (amortized spawning)
    MAX_SPAWNS_PER_FRAME: int = 3

    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        """
        1. Encuentra todas las entidades que solicitaron un spawn (SpawnRequest).
        2. Para cada solicitud (hasta MAX_SPAWNS_PER_FRAME), crea un NPC usando
           la fábrica y la información de la solicitud.
        3. Elimina la entidad que actuaba como request para limpiar el componente.
        4. Las solicitudes restantes permanecen para el siguiente frame.

        Parámetros:
          world – El objeto World que contiene entidades y sus componentes.
        """
        # Copiar las solicitudes actuales para evitar modificación durante la iteración
        requests = list(world.components.get('SpawnRequest', {}).items())

        budget = self.MAX_SPAWNS_PER_FRAME
        for req_eid, req in requests:
            if budget <= 0:
                break
            budget -= 1
            # req.prototype: identificador del tipo de NPC a generar
            # req.position: tupla (x, y) de coordenadas donde spawnar
            # Respetar instance_id si el SpawnRequest lo aporta para persistencia
            try:
                inst_id = getattr(req, 'instance_id', None)
            except Exception:
                inst_id = None
            new_eid = get_factory("monster").create(
                world,
                tile_x=req.position[0],
                tile_y=req.position[1],
                monster_type=req.prototype,
                instance_id=inst_id,
            )
            try:
                logger.debug(
                    "[SpawnSystem] Created NPC eid=%s proto=%s at tile=%s instance_id=%s",
                    new_eid,
                    getattr(req, 'prototype', None),
                    getattr(req, 'position', None),
                    inst_id,
                )
            except Exception:
                pass
            # Marcar para estabilización pos-spawn (evitar solapes iniciales sin jitter)
            world.components.setdefault('SpawnStabilizer', {})[new_eid] = SpawnStabilizer()

            # Si la solicitud indica un área de defensa, adjuntarla al NPC
            defend_center = getattr(req, 'defend_center', None)
            defend_radius_px = getattr(req, 'defend_radius_px', None)
            defend_leash = getattr(req, 'defend_leash', None)
            defend_shape = (getattr(req, 'defend_shape', None) or 'circle').lower()
            if defend_center and defend_radius_px:
                world.components.setdefault('DefendArea', {})[new_eid] = DefendArea(
                    center_x=float(defend_center[0]),
                    center_y=float(defend_center[1]),
                    radius_px=float(defend_radius_px),
                    leash=bool(True if defend_leash is None else defend_leash),
                    shape=defend_shape,
                )
                try:
                    logger.debug(
                        "[SpawnSystem] Applied DefendArea to eid=%s center=%s radius_px=%s leash=%s shape=%s",
                        new_eid, defend_center, defend_radius_px, defend_leash, defend_shape,
                    )
                except Exception:
                    pass
                # Ajustar la ruta de patrulla para circundar el área defendida
                try:
                    radius_tiles = float(defend_radius_px) / float(TILE_SIZE)
                    cx_i = int(defend_center[0]); cy_i = int(defend_center[1])
                    if defend_shape == 'square':
                        width_tiles = radius_tiles * 2.0
                        height_tiles = radius_tiles * 2.0
                        patrol_cfg = {"id": "square", "params": {"width_tiles": width_tiles, "height_tiles": height_tiles, "points_per_edge": 4}}
                    else:
                        patrol_cfg = {"id": "circle", "params": {"radius_tiles": radius_tiles, "points": 16, "clockwise": True}}
                    route = build_patrol_route(
                        cx_i,
                        cy_i,
                        patrol_cfg,
                        TILE_SIZE,
                    )
                    world.components['PatrolRoute'][new_eid] = PatrolRoute(
                        points=route.get('points', []),
                        dwell_times=route.get('dwell_times'),
                    )
                except Exception:
                    # Si la construcción de ruta falla, mantener la ruta por defecto
                    pass

                # Forzar a los defensores a entrar en Chase inmediatamente tras el spawn
                try:
                    npc_state = world.components.get('NPCState', {}).get(new_eid)
                    if npc_state is not None:
                        class _EntityProxy:
                            def __init__(self, world, entity_id):
                                self.world = world
                                self.id = entity_id
                        npc_state.fsm.change_state(ChaseState(), _EntityProxy(world, new_eid))
                except Exception:
                    # Si la FSM o el set de estados lo bloquea, continuar sin forzar
                    pass

            # Si la solicitud define un TTL, adjuntar TimedDespawn al NPC creado
            ttl = getattr(req, 'ttl_seconds', None)
            try:
                ttl_val = float(ttl) if ttl is not None else 0.0
            except Exception:
                ttl_val = 0.0
            if ttl_val > 0.0:
                try:
                    world.components.setdefault('TimedDespawn', {})[new_eid] = TimedDespawn(ttl=ttl_val)
                except Exception:
                    pass

            # Si la solicitud tiene metadatos de spawner/oleada, registrar la entidad creada
            spawner_eid = getattr(req, 'spawner_eid', None)
            wave_idx = getattr(req, 'wave_idx', None)
            if spawner_eid is not None and wave_idx is not None:
                st = world.components.get('SpawnerState', {}).get(spawner_eid)
                if st is not None:
                    # Sólo añadir si corresponde a la oleada actual
                    if st.current_wave_idx == wave_idx:
                        st.current_wave_entities.add(new_eid)
                    # Siempre registrar en el conjunto de activos del spawner
                    try:
                        st.active_entities.add(new_eid)
                    except Exception:
                        # Backward compatibility if field missing
                        pass
            # Marcar al NPC como hijo de spawner para evitar persistirlo en el guardado
            if spawner_eid is not None:
                try:
                    world.components.setdefault('SpawnerChild', {})[new_eid] = SpawnerChild(spawner_eid, wave_idx)
                    logger.info(
                        "[SpawnSystem] eid=%s marked as SpawnerChild (spawner_eid=%s, wave=%s)",
                        new_eid, spawner_eid, wave_idx
                    )
                except Exception:
                    pass

            # Una vez generado el NPC, eliminar la entidad de solicitud
            world.remove_entity(req_eid)