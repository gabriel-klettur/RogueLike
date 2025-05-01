"""
Herramientas para persistencia de capas Z.
Se usan para guardar y cargar la capa Z de entidades desde JSON.
"""

# Path: src/roguelike_game/systems/z_layer/persistence.py
from ..config_z_layer import DEFAULT_Z

def extract_z_from_json(entry, z_state=None, entity=None):
    """
    Extrae el valor de Z desde un diccionario JSON.

    Si se proporcionan `z_state` y `entity`, además de actualizar el estado central
    se actualiza la propiedad `z` del objeto entity para sincronizar la información.

    Retorna el valor de Z como entero.
    """
    raw_z = entry.get("z", DEFAULT_Z)
    try:
        z = int(raw_z)
    except (ValueError, TypeError):
        z = DEFAULT_Z

    if entity is not None:
        entity.z = z  # Se actualiza la propiedad interna del objeto
    if z_state and entity:
        z_state.set(entity, z)
    return z

def inject_z_into_json(entity, z_state):
    """
    Devuelve un valor `z` que puede ser insertado en el diccionario JSON del edificio.
    """
    return z_state.get(entity)