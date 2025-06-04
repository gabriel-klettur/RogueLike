"""
Module: inventory.py
Componente que almacena el inventario de la entidad (items).
"""
class InventoryComponent:
    """
    Componente que almacena el inventario de la entidad.
    """
    def __init__(self):
        # Lista de IDs o instancias de ítems
        self.items: list = []
