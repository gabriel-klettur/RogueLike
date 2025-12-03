"""
Object Pool para entidades de partículas.

Optimización de rendimiento: reutiliza entidades en lugar de crear/destruir
constantemente, reduciendo la presión sobre el garbage collector y el overhead
de creación de entidades.

Uso:
    pool = ParticlePool(world, initial_size=100)
    eid = pool.acquire()  # Obtener entidad del pool
    # ... usar entidad ...
    pool.release(eid)     # Devolver al pool
"""
from __future__ import annotations

from typing import TYPE_CHECKING, Set, List, Optional
import logging

if TYPE_CHECKING:
    from roguelike_game.ecs.core.manager import ECSWorld

logger = logging.getLogger(__name__)


class ParticlePool:
    """Pool reutilizable de entidades de partículas.
    
    Mantiene un conjunto de entidades pre-creadas que pueden ser
    adquiridas y liberadas sin overhead de creación/destrucción.
    
    Attributes:
        world: Referencia al ECSWorld.
        _available: Lista de entity IDs disponibles para uso.
        _in_use: Conjunto de entity IDs actualmente en uso.
        _total_created: Contador de entidades totales creadas (para métricas).
    """

    __slots__ = ('world', '_available', '_in_use', '_total_created', '_expand_size')

    def __init__(self, world: "ECSWorld", initial_size: int = 100, expand_size: int = 50) -> None:
        """Inicializa el pool con entidades pre-creadas.
        
        Args:
            world: ECSWorld donde crear las entidades.
            initial_size: Número inicial de entidades a pre-crear.
            expand_size: Número de entidades a crear cuando el pool se agota.
        """
        self.world = world
        self._available: List[int] = []
        self._in_use: Set[int] = set()
        self._total_created: int = 0
        self._expand_size: int = max(10, expand_size)
        
        if initial_size > 0:
            self._preallocate(initial_size)

    def _preallocate(self, count: int) -> None:
        """Pre-crea entidades y las añade al pool disponible.
        
        Args:
            count: Número de entidades a crear.
        """
        for _ in range(count):
            eid = self.world.create_entity()
            self._available.append(eid)
            self._total_created += 1

    def acquire(self) -> int:
        """Obtiene una entidad del pool.
        
        Si el pool está vacío, se expande automáticamente.
        
        Returns:
            Entity ID listo para usar.
        """
        if not self._available:
            self._preallocate(self._expand_size)
            if logger.isEnabledFor(logging.DEBUG):
                logger.debug(
                    "[ParticlePool] Expanded pool by %d (total: %d, in_use: %d)",
                    self._expand_size, self._total_created, len(self._in_use)
                )
        
        eid = self._available.pop()
        self._in_use.add(eid)
        return eid

    def release(self, eid: int) -> None:
        """Devuelve una entidad al pool para reutilización.
        
        Limpia todos los componentes de la entidad pero mantiene
        el entity ID para reutilización.
        
        Args:
            eid: Entity ID a liberar.
        """
        if eid not in self._in_use:
            return
        
        self._in_use.discard(eid)
        
        # Limpiar componentes de la entidad
        comps = self.world.components
        for comp_dict in comps.values():
            if isinstance(comp_dict, dict):
                comp_dict.pop(eid, None)
        
        self._available.append(eid)

    def release_all(self) -> None:
        """Libera todas las entidades en uso de vuelta al pool."""
        for eid in list(self._in_use):
            self.release(eid)

    @property
    def available_count(self) -> int:
        """Número de entidades disponibles en el pool."""
        return len(self._available)

    @property
    def in_use_count(self) -> int:
        """Número de entidades actualmente en uso."""
        return len(self._in_use)

    @property
    def total_count(self) -> int:
        """Número total de entidades creadas por este pool."""
        return self._total_created

    def get_stats(self) -> dict:
        """Retorna estadísticas del pool para debugging."""
        return {
            "available": self.available_count,
            "in_use": self.in_use_count,
            "total_created": self._total_created,
            "expand_size": self._expand_size,
        }


# Singleton global para partículas (inicializado lazy)
_global_particle_pool: Optional[ParticlePool] = None


def get_particle_pool(world: "ECSWorld") -> ParticlePool:
    """Obtiene o crea el pool global de partículas.
    
    Args:
        world: ECSWorld para crear el pool si no existe.
        
    Returns:
        ParticlePool singleton.
    """
    global _global_particle_pool
    if _global_particle_pool is None or _global_particle_pool.world is not world:
        _global_particle_pool = ParticlePool(world, initial_size=200, expand_size=100)
        logger.info("[ParticlePool] Initialized global pool with 200 entities")
    return _global_particle_pool


def reset_particle_pool() -> None:
    """Resetea el pool global (útil al cambiar de mapa/mundo)."""
    global _global_particle_pool
    _global_particle_pool = None
