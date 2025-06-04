from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
import time

class DeathState(State):
    """
    Estado Death: gestiona temporizador y eliminación de entidad muerta.
    """
    def enter(self, entity):
        """Registra temporizador."""
        world = entity.world
        eid = entity.id
        # Iniciar temporizador
        world.components['DeathTimer'][eid] = DeathTimer(time.time())
        # Cambiar el sprite al de muerte para ocultar el sprite anterior
        sprite = world.components['Sprite'].get(eid)
        if sprite and hasattr(sprite, 'death_image'):
            sprite.image = sprite.death_image
            # Deshabilitar animación para no sobreescribir el sprite de muerte
            world.components.get('Animator', {}).pop(eid, None)
            world.components.get('AnimationTimer', {}).pop(eid, None)


    def execute(self, entity, dt):
        """Espera a que expire el temporizador antes de eliminar la entidad."""
        world = entity.world
        # Obtener componente por id
        dt_cmp = world.components['DeathTimer'][entity.id]
        # Eliminar solo tras duración
        if time.time() - dt_cmp.start_time >= dt_cmp.duration:
            world.remove_entity(entity.id)

    def exit(self, entity):
        """Limpia si fuera necesario."""
        pass