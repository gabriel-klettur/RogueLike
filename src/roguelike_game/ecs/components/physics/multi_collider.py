from .collider import Collider

typing = __import__('typing')

class MultiCollider:
    """
    Componente que agrupa varios colliders para diferentes propósitos.
    colliders: dict[str, Collider] (e.g. 'feet', 'body').
    """
    def __init__(self, colliders: dict[str, Collider]):
        self.colliders = colliders
