# animation_system.py
"""
Module: animation_system.py
Updates entity animations by advancing frames and applying the current frame to the Sprite component.
"""

class AnimationSystem:
    """
    Sistema para actualizar animaciones y volcar el frame actual en Sprite.
    Recorre todos los Animator y, si hay un nuevo frame, lo aplica al componente Sprite correspondiente.
    """
    def __init__(self):
        """
        Inicializa el AnimationSystem.
        Actualmente no mantiene estado interno, pero se reserva para futura configuración.
        """
        # No hay estado interno por el momento
        pass

    def update(self, world, camera=None):
        """
        Avanza las animaciones de todas las entidades y actualiza su imagen de Sprite.

        Pasos:
        1. Obtener referencias a los mapas de componentes Animator y Sprite.
        2. Para cada entidad con Animator:
           a. Obtener el siguiente frame llamando a animator.next_frame().
           b. Si existe un frame y la entidad tiene Sprite, reemplazar sprite.image.
        """
        # 1) Cachear componentes para accesos rápidos
        comps = world.components
        anim_map = comps.get('Animator', {})   # Map id -> Animator component
        sprite_map = comps.get('Sprite', {})   # Map id -> Sprite component

        # 2) Iterar sobre cada Animator
        for eid, animator in anim_map.items():
            # Obtener el siguiente frame de animación
            frame = animator.next_frame()
            # Si hay un frame válido y la entidad tiene Sprite, aplicarlo
            if frame and eid in sprite_map:
                sprite_map[eid].image = frame
