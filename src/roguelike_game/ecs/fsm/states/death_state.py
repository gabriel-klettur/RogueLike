from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
import time

class DeathState(State):
    """
    Estado Death: gestiona temporizador y eliminación de entidad muerta.
    """
    def enter(self, entity):
        """Registra temporizador y ajusta sprite muerto."""
        world = entity.world
        eid = entity.id
        # Iniciar temporizador
        world.components['DeathTimer'][eid] = DeathTimer(time.time())
        # Acceder componentes clave
        sprite_cmp = world.components['Sprite'][eid]
        pos_cmp = world.components['Position'][eid]
        # Dimensiones sprite original
        old_w, old_h = sprite_cmp.image.get_size()
        # Asignar imagen de muerte (ya escalada en factory)
        death_img = getattr(sprite_cmp, 'death_image', None)
        if death_img:
            sprite_cmp.image = death_img
            new_w, new_h = death_img.get_size()
            # Reposicionar bottom-center
            center_x = pos_cmp.x + old_w // 2
            bottom_y = pos_cmp.y + old_h
            pos_cmp.x = center_x - new_w // 2
            pos_cmp.y = bottom_y - new_h
        # Forzar scale a 1 para no reescalar
        world.components['Scale'][eid].scale = 1.0

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