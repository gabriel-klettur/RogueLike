"""
Module: spawn_system.py
Handles conversion of SpawnRequest components into actual game entities
using the entity factory.
"""

from roguelike_game.ecs.factories.entity_factory import spawn_monster

class SpawnSystem:
    """
    Sistema que procesa componentes SpawnRequest y genera NPCs en el mundo.
    """
    def update(self, world):
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
            spawn_monster(world, req.prototype, *req.position)

            # Una vez generado el NPC, eliminar la entidad de solicitud
            world.remove_entity(req_eid)
