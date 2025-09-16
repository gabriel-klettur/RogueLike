import json
import os
from typing import Dict


class AudioConfig:
    """
    Configuración de audio (persistente en JSON) con volúmenes para:
    - music
    - ambient
    - sfx

    Rango de volumen: 0.0 .. 1.0
    """
    _instances: Dict[str, "AudioConfig"] = {}

    def __new__(cls, path: str | None = None):
        resolved = path or os.path.join(os.getcwd(), 'data', 'config', 'audio.json')
        if resolved in cls._instances:
            return cls._instances[resolved]
        inst = super().__new__(cls)
        cls._instances[resolved] = inst
        return inst

    def __init__(self, path: str | None = None):
        if getattr(self, "_initialized", False):
            return
        self._initialized = True
        self.path = path or os.path.join(os.getcwd(), 'data', 'config', 'audio.json')
        self.settings: Dict[str, float] = {
            "music": 0.6,
            "ambient": 0.6,
            "sfx": 0.7,
        }
        self._load()

    def _load(self) -> None:
        try:
            if os.path.exists(self.path):
                with open(self.path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    if isinstance(data, dict):
                        for k in ("music", "ambient", "sfx"):
                            v = data.get(k)
                            if isinstance(v, (int, float)):
                                self.settings[k] = max(0.0, min(1.0, float(v)))
        except Exception:
            # Silencioso: mantener defaults
            pass
        # Asegurar directorio
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        # Guardar si el archivo no existía
        if not os.path.exists(self.path):
            self.save()

    def get(self, key: str) -> float:
        return float(self.settings.get(key, 0.6))

    def set(self, key: str, value: float) -> None:
        if key not in ("music", "ambient", "sfx"):
            return
        self.settings[key] = max(0.0, min(1.0, float(value)))
        self.save()

    def save(self) -> None:
        try:
            # Cargar JSON existente para preservar catálogo y defaults
            data = {}
            if os.path.exists(self.path):
                try:
                    with open(self.path, 'r', encoding='utf-8') as f:
                        data = json.load(f)
                        if not isinstance(data, dict):
                            data = {}
                except Exception:
                    data = {}
            # Actualizar solo claves top-level de volumen
            data['music'] = float(self.settings.get('music', 0.6))
            data['ambient'] = float(self.settings.get('ambient', 0.6))
            data['sfx'] = float(self.settings.get('sfx', 0.7))
            with open(self.path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
        except Exception:
            pass
