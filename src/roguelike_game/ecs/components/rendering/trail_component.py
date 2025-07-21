from dataclasses import dataclass, field
import time
import pygame

@dataclass
class TrailConfig:
    """
    Configuración del rastro de sombra.
    """
    interval: float    # segundos entre snapshots
    life_time: float   # tiempo de vida de cada snapshot
    max_trails: int    # cantidad máxima de snapshots

@dataclass
class TrailSnapshot:
    """
    Snapshot de la imagen del sprite y posición.
    """
    image: pygame.Surface
    pos: tuple[int,int]
    spawn_time: float

@dataclass
class TrailComponent:
    """
    Componente que almacena la configuración y snapshots.
    """
    config: TrailConfig
    last_gen: float = field(default_factory=time.time)
    snapshots: list[TrailSnapshot] = field(default_factory=list)