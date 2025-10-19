# animation_system.py
"""
Module: animation_system.py
Updates entity animations by advancing frames and applying the current frame to the Sprite component.
"""
import time
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider

_mask_cache = {}

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
        scale_map = comps.get('Scale', {})     # Map id -> Scale component (optional)
        mc_map = comps.get('MultiCollider', {})

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
            # Actualizar Sprite solo cuando hay frame válido
            if eid in sprite_map:
                if frame is not None:
                    # Frame válido: aplicarlo
                    sprite_map[eid].image = frame

                # Sincronizar máscara de colisión (body) con el frame actual
                mc = mc_map.get(eid)
                if mc:
                    body = mc.colliders.get('body') if hasattr(mc, 'colliders') else None
                    if hasattr(body, 'mask'):
                        # 1) Intentar usar máscara precomputada en Animator
                        masks_map = getattr(animator, 'masks', {}) or {}
                        state_key = getattr(animator, 'current_state', None)
                        idx = getattr(animator, 'last_frame_idx', 0)
                        used_precomputed = False

                        if state_key and state_key in masks_map:
                            frames_masks = masks_map.get(state_key) or []
                            if 0 <= idx < len(frames_masks):
                                pmask = frames_masks[idx]
                                if pmask is not None:
                                    body.mask = pmask
                                    setattr(body, '_source_key', (state_key, idx, 'precomputed'))
                                    used_precomputed = True
                        # 2) Si no hay máscara precalculada, construir/cargar desde caché por Surface+Scale
                        if not used_precomputed:
                            surf = sprite_map[eid].image
                            # Escala del entity (no confundir con zoom de cámara)
                            scale_comp = scale_map.get(eid)
                            scale = getattr(scale_comp, 'scale', 1.0)
                            key = (id(surf), round(scale, 3))
                            cached = _mask_cache.get(key)
                            if cached is None:
                                # Construir superficie escalada si es necesario
                                if scale and scale != 1.0:
                                    w, h = surf.get_size()
                                    scaled_surf = pygame.transform.scale(surf, (int(w * scale), int(h * scale)))
                                else:
                                    scaled_surf = surf
                                mask = pygame.mask.from_surface(scaled_surf)
                                _mask_cache[key] = mask
                                cached = mask
                            # Aplicar máscara cacheada si ha cambiado la fuente
                            if getattr(body, '_source_key', None) != key or body.mask is None:
                                body.mask = cached
                                # Mantener offsets definidos por fábrica; no tocamos feet collider aquí
                                setattr(body, '_source_key', key)