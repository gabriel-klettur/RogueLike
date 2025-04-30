# src.roguelike_project/systems/z_layer/state.py

"""
Sistema central de almacenamiento para capas Z.
Asocia cada entidad (por id) con su valor de altura/capa lógica (Z).
"""

from .config_z_layer import DEFAULT_Z

class ZState:
    def __init__(self):
        # Mapa de id(entidad) → z
        self.entity_z = {}

    def set(self, entity, z):
        """Establece la capa Z de una entidad"""
        self.entity_z[id(entity)] = z

    def get(self, entity):
        """Obtiene la capa Z de una entidad (o devuelve la capa por defecto)"""
        return self.entity_z.get(id(entity), DEFAULT_Z)

    def remove(self, entity):
        """Elimina una entidad del sistema Z"""
        self.entity_z.pop(id(entity), None)

    def clear(self):
        """Borra todos los registros"""
        self.entity_z.clear()

    def all(self):
        """Devuelve una copia de todas las asignaciones"""
        return self.entity_z.copy()

    def all_in_layer(self, z):
        """Devuelve los IDs de las entidades que están en una capa Z específica"""
        return [eid for eid, layer in self.entity_z.items() if layer == z]
