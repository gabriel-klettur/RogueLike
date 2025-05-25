from ..components.animator import Animator
from ..components.sprite import Sprite

class AnimationSystem:
    """
    Sistema para actualizar animaciones y volcar el frame actual en Sprite.
    """
    def __init__(self):
        pass

    def update(self, world):
        # Cache de componentes y referencias
        comps = world.components
        anim_map = comps['Animator']
        sprite_map = comps['Sprite']
        for eid, animator in anim_map.items():
            frame = animator.next_frame()
            if frame and eid in sprite_map:
                sprite_map[eid].image = frame
