from dataclasses import dataclass

@dataclass
class GridModel:
    """
    Modelo para gestionar el estado del grid de inventario.
    """
    # Slot seleccionado en el grid
    selected_slot: int = -1
    # Hover sobre slot
    hover_slot: int = -1
    # Tamaño del grid (filas x columnas)
    grid_rows: int = 5
    grid_cols: int = 5
