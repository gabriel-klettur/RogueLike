import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

logger = logging.getLogger(__name__)

# Ruta al directorio raíz del proyecto
BASE_DIR = Path(__file__).resolve().parents[3]


@dataclass
class SpellConfig:
    """Typed view over a spell config entry with sensible defaults.
    It remains backwards-compatible with existing code via .get().
    """
    key: str
    type: str = ""

    # Basic identity
    id: str = ""
    name: str = ""

    # Casting rules
    max_instances: int = 0
    allow_overlap: bool = True
    allow_movement: bool = False
    interruptible: bool = False
    automatic: bool = False
    automatic_cast_punish: float = 1.0

    # Common projectile-like fields
    speed: float = 0.0
    damage: float = 0.0
    lifespan: float = 0.0
    range: float = 0.0
    sprite: Optional[str] = None
    scale: float = 1.0

    # Area/auras
    radius: float = 0.0
    duration: float = 0.0
    buff: Dict[str, Any] = field(default_factory=dict)

    # Particles / beam
    particle_count: int = 0
    particle_dispersion: float = 0.0
    particle_colors: List[Tuple[int, int, int]] = field(default_factory=list)

    # Lightning
    segments: int = 0
    offset: float = 0.0
    lifetime: float = 0.0

    # Teleport
    distance: float = 0.0

    # Slash
    size_range: List[float] = field(default_factory=lambda: [1.0, 1.0])
    color: List[int] = field(default_factory=lambda: [255, 255, 255])
    speed_multiplier: float = 1.0
    arc_range_degrees: float = 0.0

    # Smoke emitter
    particle_color: List[int] = field(default_factory=lambda: [200, 200, 200])
    emit_rate: int = 0

    # Durations (FSM phases)
    prepare_duration: float = 0.0
    channel_duration: float = 0.0
    cooldown_duration: float = 0.0
    lock_cast_direction: bool = True

    # VFX and particles
    vfx: Optional[str] = None
    particle_lifespan: float = 0.0
    particle_speed: float = 0.0

    # Unknown/extended fields retained for backward compatibility
    extra: Dict[str, Any] = field(default_factory=dict)

    # Maintain compatibility with dict-like access
    def get(self, key: str, default: Any = None) -> Any:
        if hasattr(self, key):
            return getattr(self, key)
        return self.extra.get(key, default)


# Keys exposed for schema-driven editors (no external dependency required)
SCHEMA_KEYS: List[str] = [
    # Identity and routing
    "type", "id", "name",
    # Casting rules
    "max_instances", "allow_overlap", "allow_movement", "interruptible",
    "automatic", "automatic_cast_punish",
    # Common
    "speed", "damage", "lifespan", "range", "sprite", "scale",
    # Areas / durations
    "radius", "duration", "buff",
    # Particles / beams
    "particle_count", "particle_dispersion", "particle_colors",
    # Lightning
    "segments", "offset", "lifetime",
    # Teleport
    "distance",
    # Slash
    "size_range", "color", "speed_multiplier", "arc_range_degrees",
    # Smoke emitter
    "particle_color", "emit_rate",
    # FSM phase durations & controls
    "prepare_duration", "channel_duration", "cooldown_duration", "lock_cast_direction",
    # VFX and other particles
    "vfx", "particle_lifespan", "particle_speed",
]


def _coerce_types(spell_key: str, data: Dict[str, Any]) -> SpellConfig:
    """Build a SpellConfig from raw dict, ignoring unknown keys and coercing simple types."""
    kwargs: Dict[str, Any] = {k: v for k, v in data.items() if k in SCHEMA_KEYS}
    extras: Dict[str, Any] = {k: v for k, v in data.items() if k not in kwargs}
    # Basic normalizations
    if isinstance(kwargs.get("size_range"), tuple):
        kwargs["size_range"] = list(kwargs["size_range"])  # json-friendly
    if isinstance(kwargs.get("color"), tuple):
        kwargs["color"] = list(kwargs["color"])
    if isinstance(kwargs.get("particle_color"), tuple):
        kwargs["particle_color"] = list(kwargs["particle_color"])
    if isinstance(kwargs.get("particle_colors"), tuple):
        kwargs["particle_colors"] = list(kwargs["particle_colors"])

    cfg = SpellConfig(key=spell_key, **kwargs, extra=extras)  # type: ignore[arg-type]
    # Minimal required validation
    if not cfg.type:
        logger.warning(f"Spell '{spell_key}' missing required field 'type'.")
    return cfg


def load_spells_config(json_path: Path) -> Dict[str, SpellConfig]:
    with open(json_path, "r", encoding="utf-8") as f:
        raw: Dict[str, Dict[str, Any]] = json.load(f)
    # Optional: validate entire file against schema.json if available and jsonschema installed
    try:
        from jsonschema import Draft7Validator  # type: ignore
        schema_path = json_path.parent / "schema.json"
        if schema_path.exists():
            with open(schema_path, "r", encoding="utf-8") as sf:
                schema = json.load(sf)
            validator = Draft7Validator(schema)
            for err in validator.iter_errors(raw):
                path = "/".join([str(p) for p in err.path])
                logger.warning(f"spells.json schema error at '{path}': {err.message}")
    except Exception as exc:
        # Do not crash the game/editor if validation is unavailable
        logger.debug(f"jsonschema validation skipped: {exc}")
    typed: Dict[str, SpellConfig] = {}
    for key, data in raw.items():
        try:
            typed[key] = _coerce_types(key, data or {})
        except Exception as exc:
            logger.exception(f"Error parsing spell '{key}': {exc}")
    return typed


# Cargar configuración de hechizos (typed)
SPELLS: Dict[str, SpellConfig] = load_spells_config(BASE_DIR / "data" / "spells" / "spells.json")