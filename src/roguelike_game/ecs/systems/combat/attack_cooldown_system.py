import time

class AttackCooldownSystem:
    """
    Sistema responsable de purgar componentes de cooldown de ataque
    que hayan expirado hace demasiado tiempo.
    """

    def update(self, world):
        """
        Recorre todos los AttackCooldown activos y elimina aquellos
        cuyo timestamp de próxima acción (next_time) esté atrasado
        más de un margen de gracia (60 segundos).
        """
        now = time.time()
        # Iterar sobre una copia de los cooldowns para poder modificar el dict
        for eid, cd in list(world.components['AttackCooldown'].items()):
            # Si han pasado 60 segundos desde next_time, eliminar el componente
            if now >= cd.next_time + 60:
                del world.components['AttackCooldown'][eid]
