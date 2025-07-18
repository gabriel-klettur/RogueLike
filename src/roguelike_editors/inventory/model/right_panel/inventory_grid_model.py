from dataclasses import dataclass, field
from typing import List, Optional

@dataclass
class InventoryGridModel:
    """
    Modelo para gestionar el flujo de añadir y eliminar ítems en el grid.
    """
    # Lista de todos los ítems disponibles (identificadores)
    available_items: List[str] = field(default_factory=list)
    # Mostrar lista de ítems para selección
    show_item_list: bool = False
    # Ítem seleccionado (identificador)
    selected_item: Optional[str] = None
    # Mostrar input de cantidad tras seleccionar ítem
    show_quantity_input: bool = False
    # Cantidad a agregar
    quantity: int = 1
