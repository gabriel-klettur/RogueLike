# Path: src/roguelike_engine/cache/memory_cache.py
import time
from collections import OrderedDict
from .icache import ICache

class MemoryCache(ICache):
    """
    Caché en memoria con política LRU y TTL opcional.
    """
    def __init__(self, max_size=None):
        self.max_size = max_size
        self.store = OrderedDict()  # key -> (value, expire_time)

    def get(self, key: str):
        item = self.store.get(key)
        if item is None:
            return None
        value, expire = item
        # TTL expirado
        if expire is not None and time.time() > expire:
            del self.store[key]
            return None
        # mover a reciente
        self.store.move_to_end(key)
        return value

    def put(self, key: str, value, ttl: int = None) -> None:
        expire = time.time() + ttl if ttl else None
        # sustituir
        if key in self.store:
            del self.store[key]
        self.store[key] = (value, expire)
        self.store.move_to_end(key)
        # LRU evicción
        if self.max_size is not None and len(self.store) > self.max_size:
            self.store.popitem(last=False)

    def invalidate(self, key: str) -> None:
        self.store.pop(key, None)

    def clear(self) -> None:
        self.store.clear()