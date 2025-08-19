"""
Module: spawn_system.py
Handles conversion of SpawnRequest components into actual game entities
using the entity factory.
"""
from roguelike_game.factories.registry import get_factory
from roguelike_game.ecs.components.spawn.spawn_stabilizer import SpawnStabilizer
from roguelike_engine.utils.benchmark import benchmark

class SpawnSystem:
    """
    Sistema que procesa componentes SpawnRequest y genera NPCs en el mundo.
    """

    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SpawnSystem.update")
    def update(self, world, camera=None):
        """
        1. Encuentra todas las entidades que solicitaron un spawn (SpawnRequest).
        2. Para cada solicitud, crea un NPC usando spawn_monster y la información de la solicitud.
        3. Elimina la entidad que actuaba como request para limpiar el componente.

        Parámetros:
          world – El objeto World que contiene entidades y sus componentes.
        """
        # Copiar las solicitudes actuales para evitar modificación durante la iteración
        requests = list(world.components.get('SpawnRequest', {}).items())

        for req_eid, req in requests:
            # req.prototype: identificador del tipo de NPC a generar
            # req.position: tupla (x, y) de coordenadas donde spawnar
            new_eid = get_factory("monster").create(world, tile_x=req.position[0], tile_y=req.position[1], monster_type=req.prototype)
            # Marcar para estabilización pos-spawn (evitar solapes iniciales sin jitter)
            world.components.setdefault('SpawnStabilizer', {})[new_eid] = SpawnStabilizer()

            # Si la solicitud tiene metadatos de spawner/oleada, registrar la entidad creada
            spawner_eid = getattr(req, 'spawner_eid', None)
            wave_idx = getattr(req, 'wave_idx', None)
            if spawner_eid is not None and wave_idx is not None:
                st = world.components.get('SpawnerState', {}).get(spawner_eid)
                if st is not None:
                    # Sólo añadir si corresponde a la oleada actual
                    if st.current_wave_idx == wave_idx:
                        st.current_wave_entities.add(new_eid)

            # Una vez generado el NPC, eliminar la entidad de solicitud
            world.remove_entity(req_eid)