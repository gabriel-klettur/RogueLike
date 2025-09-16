from __future__ import annotations
from typing import Callable, Dict, List, Any


class EventBus:
    """
    Bus de eventos simple para el paquete world.
    Eventos soportados (sugeridos):
      - on_before_save(snapshot_dict)
      - on_after_save(path, duration_ms)
      - on_level_loaded(level_name)
      - on_level_unloaded(level_name)
      - on_slot_changed(path)
    """

    def __init__(self):
        self._listeners: Dict[str, List[Callable[..., None]]] = {}

    def subscribe(self, event: str, callback: Callable[..., None]) -> None:
        self._listeners.setdefault(event, []).append(callback)

    def publish(self, event: str, *args: Any, **kwargs: Any) -> None:
        for cb in self._listeners.get(event, []):
            try:
                cb(*args, **kwargs)
            except Exception:
                # Aislamos errores de listeners para no romper el flujo
                pass
