import time
import pickle
from pathlib import Path
from .icache import ICache

class FileCache(ICache):
    """
    Caché en disco usando pickle. TTL opcional (segundos)."""
    def __init__(self, dir_path, ttl: int = None):
        self.dir = Path(dir_path)
        self.dir.mkdir(parents=True, exist_ok=True)
        self.ttl = ttl

    def _path(self, key: str) -> Path:
        return self.dir / f"{key}.pkl"

    def get(self, key: str):
        path = self._path(key)
        if not path.exists():
            return None
        if self.ttl is not None:
            age = time.time() - path.stat().st_mtime
            if age > self.ttl:
                path.unlink(missing_ok=True)
                return None
        try:
            with open(path, 'rb') as f:
                return pickle.load(f)
        except Exception:
            # Invalida fichero corrupto
            path.unlink(missing_ok=True)
            return None

    def put(self, key: str, value, ttl: int = None) -> None:
        path = self._path(key)
        try:
            with open(path, 'wb') as f:
                pickle.dump(value, f)
        except Exception:
            # No crash
            pass

    def invalidate(self, key: str) -> None:
        path = self._path(key)
        path.unlink(missing_ok=True)

    def clear(self) -> None:
        for path in self.dir.glob('*.pkl'):
            path.unlink(missing_ok=True)
