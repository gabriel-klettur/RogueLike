# roguelike_project/systems/z_layer/render.py

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
    entities_sorted = sorted(
        entities,
        key=lambda e: (z_state.get(e), getattr(e, "y", 0))
    )

    for entity in entities_sorted:
        entity.render(screen, camera)
