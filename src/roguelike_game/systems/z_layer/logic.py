# Path: src/roguelike_game/systems/z_layer/logic.py

"""
Funciones de lógica relacionadas con las capas Z.
Controla colisiones, movimientos entre capas y comparaciones.
"""

def can_collide(e1, e2, z_state):
    """
    Determina si dos entidades pueden colisionar.
    Solo pueden colisionar si están en la misma capa Z.
    """
    return z_state.get(e1) == z_state.get(e2)

def switch_layer(entity, z_state, new_z):
    """
    Cambia la capa Z de una entidad.
    """
    z_state.set(entity, new_z)

def are_on_same_layer(e1, e2, z_state):
    """
    Devuelve True si dos entidades comparten la misma capa.
    """
    return z_state.get(e1) == z_state.get(e2)

def is_above(e1, e2, z_state):
    """
    True si e1 está en una capa superior a e2.
    """
    return z_state.get(e1) > z_state.get(e2)

def is_below(e1, e2, z_state):
    """
    True si e1 está en una capa inferior a e2.
    """
    return z_state.get(e1) < z_state.get(e2)
