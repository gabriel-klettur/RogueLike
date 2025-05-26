from ..components.spawn_request import SpawnRequest
from ..factories.entity_factory import spawn_monster

class SpawnSystem:
    """Consume SpawnRequest y crea NPCs reales."""
    def update(self, world):
        # Obtener todas las requests actuales
        requests = list(world.components.get('SpawnRequest', {}).items())
        for req_eid, req in requests:
            # Crear NPC real usando entity_factory
            spawn_monster(world, req.prototype, *req.position)
            # Eliminar la entidad request
            world.remove_entity(req_eid)
