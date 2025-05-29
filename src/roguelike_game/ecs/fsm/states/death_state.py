from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
import time

class DeathState(State):
    """
    Estado Death: gestiona temporizador y eliminación de entidad muerta.
    """
    def enter(self, entity):
        """Registra inicio de temporizador de muerte."""
        world = entity.world
        world.components['DeathTimer'][entity] = DeathTimer(time.time())

    def execute(self, entity, dt):
        """Elimina inmediatamente la entidad del mundo."""
        entity.world.remove_entity(entity.id)

    def exit(self, entity):
        """Limpia si fuera necesario."""
        pass