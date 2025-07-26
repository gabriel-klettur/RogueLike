from dataclasses import dataclass

@dataclass
class SaveModel:
    """
    Modelo para gestionar el flujo de guardar datos del inventario.
    """
    # Estado de guardado
    save_in_progress: bool = False
    # Mensaje de estado del guardado
    save_message: str = ""
