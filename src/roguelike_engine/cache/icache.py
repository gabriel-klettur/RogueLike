# Path: src/roguelike_engine/cache/icache.py
from abc import ABC, abstractmethod

class ICache(ABC):
    """
    Interfaz genérica de cache.
    """
    @abstractmethod
    def get(self, key: str):
        """Devuelve el valor asociado o None si no existe o ha expirado."""
        pass

    @abstractmethod
    def put(self, key: str, value, ttl: int = None) -> None:
        """Almacena un valor con clave `key` y tiempo de vida opcional en segundos."""
        pass

    @abstractmethod
    def invalidate(self, key: str) -> None:
        """Elimina la entrada de cache para `key`."""
        pass

    @abstractmethod
    def clear(self) -> None:
        """Limpia todas las entradas de cache."""
        pass