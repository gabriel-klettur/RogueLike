# animation_system.py
"""
Module: animation_system.py
Updates entity animations by advancing frames and applying the current frame to the Sprite component.
"""

import time
from roguelike_engine.utils.benchmark import benchmark

class AnimationSystem:
    """
    Sistema para actualizar animaciones y volcar el frame actual en Sprite.
    Recorre todos los Animator y, si hay un nuevo frame, lo aplica al componente Sprite correspondiente.
    """
    def __init__(self, perf_log=None):
        """
        Inicializa el AnimationSystem.
        Actualmente no mantiene estado interno, pero se reserva para futura configuración.
        """
        # No hay estado interno por el momento
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.AnimationSystem.update")
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
        timer_map = comps.get('AnimationTimer', {})
        anim_map = comps.get('Animator', {})   # Map id -> Animator component
        sprite_map = comps.get('Sprite', {})   # Map id -> Sprite component

        # 2) Iterar sobre cada Animator
        for eid, animator in anim_map.items():
            timer = timer_map.get(eid)
            now = time.time()
            if timer and now - timer.last_time < timer.interval:
                continue
            if timer:
                timer.last_time = now
            # Obtener el siguiente frame de animación
            frame = animator.next_frame()
            # Si hay un frame válido y la entidad tiene Sprite, aplicarlo
            if frame and eid in sprite_map:
                sprite_map[eid].image = frame
