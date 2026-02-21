import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Optional

logger = logging.getLogger(__name__)

# Ruta al directorio raíz del proyecto (igual a spells_config.py)
BASE_DIR = Path(__file__).resolve().parents[3]
PARTICLES_PATH = BASE_DIR / "data" / "particles" / "particles.json"


@dataclass
class ParticleEffectConfig:
    """Vista tipada de una entrada del catálogo de partículas.

    No fuerza un esquema rígido; conserva cualquier campo adicional en `extra`.
    """
    id: str
    name: str = ""
    type: str = ""
    # Referencia a clase de sistema o modelo (opcional)
    system: Dict[str, Any] = field(default_factory=dict)
    model: Dict[str, Any] = field(default_factory=dict)
    # Bloque VFX completo para previews o defaults (preview kind/params)
    vfx: Dict[str, Any] = field(default_factory=dict)
    # Campos desconocidos preservados
    extra: Dict[str, Any] = field(default_factory=dict)

    # Compatibilidad estilo dict
    def get(self, key: str, default: Any = None) -> Any:
        if hasattr(self, key):
            return getattr(self, key)
        return self.extra.get(key, default)

    def __contains__(self, key: str) -> bool:  # type: ignore[override]
        return hasattr(self, key) or (key in self.extra)


def _coerce_effect(key: str, data: Dict[str, Any]) -> ParticleEffectConfig:
    # Copiar campos conocidos
    known: Dict[str, Any] = {k: v for k, v in data.items() if k in ("id", "name", "type", "system", "model", "vfx")}
    # Asegurar id
    if "id" not in known:
        known["id"] = key
    # Extras
    extras: Dict[str, Any] = {k: v for k, v in data.items() if k not in known}
    return ParticleEffectConfig(**known, extra=extras)  # type: ignore[arg-type]


def load_particles_config(json_path: Path = PARTICLES_PATH) -> Dict[str, ParticleEffectConfig]:
    with open(json_path, "r", encoding="utf-8") as f:
        raw: Dict[str, Dict[str, Any]] = json.load(f)
    typed: Dict[str, ParticleEffectConfig] = {}
    for key, data in (raw or {}).items():
        try:
            typed[key] = _coerce_effect(key, data or {})
        except Exception as exc:
            logger.exception("Error parsing particle effect '%s': %s", key, exc)
    return typed


# Catálogo global (similar a SPELLS)
PARTICLES: Dict[str, ParticleEffectConfig] = load_particles_config()
PARTICLES_VERSION: int = 0


def reload_particles() -> None:
    """Recarga particles.json y actualiza el dict global in-place.

    Mantener el mismo objeto asegura que los módulos que importaron PARTICLES
    por nombre vean los cambios.
    """
    try:
        new_data = load_particles_config(PARTICLES_PATH)
        PARTICLES.clear()
        PARTICLES.update(new_data)
        global PARTICLES_VERSION
        PARTICLES_VERSION += 1
        logger.info("[particles_config] Reloaded particles.json: %d entries (version=%d)", len(PARTICLES), PARTICLES_VERSION)
    except Exception:
        logger.exception("[particles_config] Failed to reload particles.json")


def get_preset(preset_id: str) -> Optional[ParticleEffectConfig]:
    """Acceso cómodo a un preset por id (None si no existe)."""
    try:
        return PARTICLES.get(preset_id)
    except Exception:
        return None
