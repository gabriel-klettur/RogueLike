# src/roguelike_engine/world/world_config.py

from pathlib import Path
from dataclasses import dataclass

@dataclass(frozen=True)
class WorldConfig:
    # Directorio donde se guardan los archivos de estado del mundo
    save_dir: Path = Path.cwd() / "saves"
    # Nombre por defecto del archivo de guardado global
    save_file: str = "world_state.json"
    # Número máximo de niveles cargados simultáneamente en memoria
    max_loaded_levels: int = 3
    # Habilitar autoguardado periódico
    autosave_enabled: bool = True
    # Intervalo de autoguardado (segundos)
    autosave_interval: int = 300  # 5 minutos

    @property
    def save_path(self) -> Path:
        """
        Ruta completa al archivo de guardado global.
        """
        return self.save_dir / self.save_file

# Instancia global para usar desde cualquier parte
WORLD_CONFIG = WorldConfig()
