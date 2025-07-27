from dataclasses import dataclass

@dataclass
class DeleteModel:
    """
    Modelo para gestionar el flujo de eliminar ítems del grid.
    """
    # Modo eliminación activa
    show_delete_mode: bool = False
    # Mostrar input de cantidad para eliminación
    show_delete_quantity_input: bool = False
    # Cantidad de ítems a eliminar
    delete_quantity: int = 1
