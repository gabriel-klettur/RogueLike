from __future__ import annotations
import json
from pathlib import Path
from typing import Dict, Any, Optional

DEFAULT_CONFIG_PATH = Path('data/config/audio.json')

class AudioCatalog:
    def __init__(self, data: Dict[str, Any] | None = None):
        data = data or {}
        self.tracks: Dict[str, Dict[str, Any]] = data.get('tracks', {}) or {}
        # Usar 'sfx_map' para no colisionar con el volumen 'sfx' top-level
        self.sfx: Dict[str, Dict[str, Any]] = data.get('sfx_map', {}) or {}
        self.groups: Dict[str, Dict[str, Any]] = data.get('groups', {}) or {
            'music': {'volume': 0.6},
            'sfx': {'volume': 0.7},
            'ambient': {'volume': 0.6},
        }
        # Campos avanzados
        self.defaults: Dict[str, Any] = data.get('defaults', {}) or {}
        self.biomes: Dict[str, Any] = data.get('biomes', {}) or {}
        self.levels: Dict[str, Any] = data.get('levels', {}) or {}
        self.zones: Dict[str, Any] = data.get('zones', {}) or {}
        # Índices case-insensitive para zonas/biomas/levels
        self._zones_ci = {str(k).lower(): v for k, v in (self.zones or {}).items()}
        self._biomes_ci = {str(k).lower(): v for k, v in (self.biomes or {}).items()}
        self._levels_ci = {str(k).lower(): v for k, v in (self.levels or {}).items()}

    def track_path(self, track_id: str) -> Optional[str]:
        t = self.tracks.get(track_id)
        return (t or {}).get('path') if t else None

    def sfx_path(self, sfx_id: str) -> Optional[str]:
        s = self.sfx.get(sfx_id)
        return (s or {}).get('path') if s else None

    # ---- Helpers de ámbito ----
    def get_default_music(self) -> Dict[str, Any]:
        return self.defaults.get('music', {}) or {}

    def get_default_ambient(self) -> Dict[str, Any]:
        return self.defaults.get('ambient', {}) or {}

    def get_default_ducking(self) -> Dict[str, Any]:
        return self.defaults.get('ducking', {}) or {}

    def resolve_music_for(self, *, level: Optional[str] = None, zone: Optional[str] = None, biome: Optional[str] = None) -> Optional[str]:
        # Prioridad: zone > level > biome > defaults
        if zone:
            zkey = str(zone)
            zrec = self.zones.get(zkey)
            if zrec is None:
                zrec = self._zones_ci.get(zkey.lower())
        else:
            zrec = None
        if isinstance(zrec, dict):
            t = zrec.get('music_track_id')
            if t:
                return t
        if level:
            lkey = str(level)
            lrec = self.levels.get(lkey) or self._levels_ci.get(lkey.lower())
        else:
            lrec = None
        if isinstance(lrec, dict):
            t = lrec.get('music_track_id')
            if t:
                return t
        if biome:
            bkey = str(biome)
            brec = self.biomes.get(bkey) or self._biomes_ci.get(bkey.lower())
        else:
            brec = None
        if isinstance(brec, dict):
            t = brec.get('music_track_id')
            if t:
                return t
        return (self.get_default_music() or {}).get('ingame_track_id')

    def resolve_ambient_for(self, *, level: Optional[str] = None, zone: Optional[str] = None, biome: Optional[str] = None) -> Dict[str, Any]:
        base = dict(self.get_default_ambient() or {})
        # Aplicar overrides si existen (merge superficial)
        scopes = []
        # Zona
        zrec = None
        if zone:
            zkey = str(zone)
            zrec = self.zones.get(zkey) or self._zones_ci.get(zkey.lower())
        if isinstance(zrec, dict):
            if isinstance(zrec.get('ambient'), dict):
                scopes.append(zrec['ambient'])
        # Nivel
        lrec = None
        if level:
            lkey = str(level)
            lrec = self.levels.get(lkey) or self._levels_ci.get(lkey.lower())
        if isinstance(lrec, dict):
            if isinstance(lrec.get('ambient'), dict):
                scopes.append(lrec['ambient'])
        # Bioma
        brec = None
        if biome:
            bkey = str(biome)
            brec = self.biomes.get(bkey) or self._biomes_ci.get(bkey.lower())
        if isinstance(brec, dict):
            if isinstance(brec.get('ambient'), dict):
                scopes.append(brec['ambient'])
        for ov in scopes:
            base.update({k: v for k, v in ov.items() if v is not None})
        return base


def load_audio_catalog(path: Path = DEFAULT_CONFIG_PATH) -> AudioCatalog:
    try:
        if path.exists():
            data = json.loads(path.read_text(encoding='utf-8'))
            return AudioCatalog(data)
    except Exception:
        pass
    return AudioCatalog({})
