from ..components.animator import Animator
from ..components.sprite import Sprite

class AnimationSystem:
    """
    Sistema para actualizar animaciones y volcar el frame actual en Sprite.
    """
    def __init__(self):
        pass

    def update(self, world):
        for eid in world.get_entities_with('Animator', 'Sprite'):
            animator: Animator = world.components['Animator'][eid]
            sprite_comp: Sprite = world.components['Sprite'][eid]
            frame = animator.next_frame()
            if frame is not None:
                sprite_comp.image = frame
