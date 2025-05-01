#Path: src/roguelike_engine/map/exporter/factory.py

from .debug_txt_exporter import DebugTxtExporter
from .interfaces import MapExporter

_exporters: dict[str, type[MapExporter]] = {
    "debug_txt": DebugTxtExporter,
    # en el futuro: "json": JsonExporter, "image": ImageExporter...
}

def get_exporter(name: str = "debug_txt", **kwargs) -> MapExporter:
    """
    Devuelve una instancia del exportador registrado bajo `name`.
    kwargs se pasan al constructor de la clase.
    """
    cls = _exporters.get(name)
    if cls is None:
        raise ValueError(f"Exportador desconocido: {name!r}")
    return cls(**kwargs)
