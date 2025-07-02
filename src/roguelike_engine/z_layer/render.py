"""
Renderizado ordenado por capas Z y eje Y.
Permite controlar qué entidades se dibujan encima o debajo de otras,
simulando profundidad en una vista top-down.
"""

def render_z_ordered(entities, screen, camera, z_state):
    """
    Renderiza una lista de entidades ordenadas por:
    1. Capa Z (más baja primero)
    2. Posición vertical Y (más arriba primero dentro de la misma capa)
    """
    # Agrupar entidades por capa Z y ordenar cada bucket por Y
    layers = {}
    for e in entities:
        z = z_state.get(e)
        layers.setdefault(z, []).append(e)
    # Iterar capas en orden ascendente
    for z in sorted(layers):
        bucket = layers[z]
        # Ordenar bucket por coordenada Y
        bucket.sort(key=lambda ent: getattr(ent, "y", 0))
        for entity in bucket:
            entity.render(screen, camera)
