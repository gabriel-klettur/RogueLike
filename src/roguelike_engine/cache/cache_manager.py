# Path: src/roguelike_engine/cache/cache_manager.py
from .icache import ICache

class CacheManager:
    """
    Administra múltiples caches por namespace.
    """
    def __init__(self):
        self._caches: dict[str, ICache] = {}

    def register(self, namespace: str, cache: ICache) -> None:
        """Registra un cache bajo un namespace único."""
        if namespace in self._caches:
            raise ValueError(f"Namespace '{namespace}' ya registrado")
        self._caches[namespace] = cache

    def get_cache(self, namespace: str) -> ICache:
        """Obtiene la instancia de cache registrada."""
        try:
            return self._caches[namespace]
        except KeyError:
            raise KeyError(f"Namespace '{namespace}' no encontrado")

    def invalidate(self, namespace: str, key: str) -> None:
        """Invalidar una clave en el cache especificado."""
        cache = self.get_cache(namespace)
        cache.invalidate(key)

    def clear(self, namespace: str) -> None:
        """Limpia todas las entradas del cache especificado."""
        cache = self.get_cache(namespace)
        cache.clear()