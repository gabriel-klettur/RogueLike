"""
Renderizado ordenado por capas Z y eje Y.
Permite controlar qué entidades se dibujan encima o debajo de otras,
simulando profundidad en una vista top-down.
"""
from operator import attrgetter

# Pre-create key function to avoid lambda overhead each frame
_y_key = attrgetter("y")


def render_z_ordered(entities, screen, camera, z_state):
    """
    Renderiza una lista de entidades ordenadas por:
    1. Capa Z (más baja primero)
    2. Posición vertical Y (más arriba primero dentro de la misma capa)
    
    Optimized: uses attrgetter instead of lambda, and direct iteration.
    """
    # Fast path: very few entities don't need complex sorting
    count = len(entities)
    if count == 0:
        return
    if count == 1:
        entities[0].render(screen, camera)
        return

    # Agrupar entidades por capa Z
    layers: dict = {}
    for e in entities:
        z = z_state.get(e)
        bucket = layers.get(z)
        if bucket is None:
            layers[z] = [e]
        else:
            bucket.append(e)

    # Iterar capas en orden ascendente y ordenar cada bucket por Y
    for z in sorted(layers):
        bucket = layers[z]
        # Only sort if bucket has multiple items
        if len(bucket) > 1:
            bucket.sort(key=_y_key)
        for entity in bucket:
            entity.render(screen, camera)
