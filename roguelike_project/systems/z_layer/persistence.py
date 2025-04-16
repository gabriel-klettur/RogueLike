# roguelike_project/systems/z_layer/persistence.py

"""
Herramientas para persistencia de capas Z.
Se usan para guardar y cargar la capa Z de entidades desde JSON.
"""

from .config import DEFAULT_Z

def extract_z_from_json(entry, z_state=None, entity=None):
    """
    Extrae el valor de Z desde un diccionario JSON.

    Si `z_state` y `entity` se proporcionan, también se aplica directamente.
    """
    z = entry.get("z", DEFAULT_Z)
    if z_state and entity:
        z_state.set(entity, z)
    return z

def inject_z_into_json(entity, z_state):
    """
    Devuelve un valor `z` que puede ser insertado en el diccionario JSON del edificio.
    """
    return z_state.get(entity)
