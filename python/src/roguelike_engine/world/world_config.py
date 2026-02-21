from pathlib import Path
from dataclasses import dataclass

@dataclass(frozen=True)
class WorldConfig:
    # Directorio base donde se guardan los slots de partida (partida_YYYY-MM-DD_HH-MM-SS.json)
    save_dir: Path = Path.cwd() / "data" / "saves"
    # Número máximo de niveles cargados simultáneamente en memoria
    max_loaded_levels: int = 3
    # Habilitar autoguardado periódico
    autosave_enabled: bool = True
    # Intervalo de autoguardado (segundos)
    autosave_interval: int = 300  # 5 minutos

# Instancia global para usar desde cualquier parte
WORLD_CONFIG = WorldConfig()