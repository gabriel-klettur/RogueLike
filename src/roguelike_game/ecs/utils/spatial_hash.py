"""
Spatial Hash Grid para queries de colisión eficientes.

Optimización de rendimiento: reduce la complejidad de detección de colisiones
de O(n²) a O(n) amortizado usando una grilla espacial.

Uso:
    grid = SpatialHash(cell_size=64)
    grid.insert(entity_id, x, y, radius)
    nearby = grid.query_radius(x, y, search_radius)
"""
from __future__ import annotations

from typing import Dict, Set, Tuple, Iterable, Optional
import logging

logger = logging.getLogger(__name__)


class SpatialHash:
    """Grid espacial para queries de colisión O(1) amortizado.
    
    Divide el espacio en celdas de tamaño fijo y mantiene un índice
    de qué entidades están en cada celda. Las queries solo revisan
    las celdas relevantes en lugar de todas las entidades.
    
    Attributes:
        cell_size: Tamaño de cada celda en píxeles.
        _cells: Diccionario de (cell_x, cell_y) -> set de entity IDs.
        _entity_cells: Diccionario de entity_id -> set de celdas donde está.
    """

    __slots__ = ('cell_size', '_cells', '_entity_cells', '_inv_cell_size')

    def __init__(self, cell_size: int = 64) -> None:
        """Inicializa el spatial hash.
        
        Args:
            cell_size: Tamaño de cada celda en píxeles. Valores típicos: 32-128.
                       Celdas más pequeñas = menos falsos positivos pero más memoria.
        """
        self.cell_size: int = max(16, cell_size)
        self._inv_cell_size: float = 1.0 / self.cell_size
        self._cells: Dict[Tuple[int, int], Set[int]] = {}
        self._entity_cells: Dict[int, Set[Tuple[int, int]]] = {}

    def _cell_key(self, x: float, y: float) -> Tuple[int, int]:
        """Calcula la celda para una posición dada."""
        return int(x * self._inv_cell_size), int(y * self._inv_cell_size)

    def _get_cell_range(
        self, x: float, y: float, radius: float
    ) -> Tuple[int, int, int, int]:
        """Calcula el rango de celdas que cubre un círculo."""
        x1 = int((x - radius) * self._inv_cell_size)
        y1 = int((y - radius) * self._inv_cell_size)
        x2 = int((x + radius) * self._inv_cell_size)
        y2 = int((y + radius) * self._inv_cell_size)
        return x1, y1, x2, y2

    def insert(self, eid: int, x: float, y: float, radius: float = 0.0) -> None:
        """Inserta una entidad en el spatial hash.
        
        La entidad se inserta en todas las celdas que cubre su AABB.
        
        Args:
            eid: Entity ID.
            x: Posición X del centro.
            y: Posición Y del centro.
            radius: Radio de la entidad (0 para punto).
        """
        x1, y1, x2, y2 = self._get_cell_range(x, y, radius)
        
        cells_for_entity = self._entity_cells.setdefault(eid, set())
        
        for cx in range(x1, x2 + 1):
            for cy in range(y1, y2 + 1):
                key = (cx, cy)
                self._cells.setdefault(key, set()).add(eid)
                cells_for_entity.add(key)

    def remove(self, eid: int) -> None:
        """Elimina una entidad del spatial hash.
        
        Args:
            eid: Entity ID a eliminar.
        """
        cells = self._entity_cells.pop(eid, None)
        if cells:
            for key in cells:
                cell_set = self._cells.get(key)
                if cell_set:
                    cell_set.discard(eid)
                    if not cell_set:
                        del self._cells[key]

    def update(self, eid: int, x: float, y: float, radius: float = 0.0) -> None:
        """Actualiza la posición de una entidad.
        
        Más eficiente que remove + insert si la entidad se mueve poco.
        
        Args:
            eid: Entity ID.
            x: Nueva posición X.
            y: Nueva posición Y.
            radius: Radio de la entidad.
        """
        # Por simplicidad, remove + insert. Optimizar si es bottleneck.
        self.remove(eid)
        self.insert(eid, x, y, radius)

    def query_point(self, x: float, y: float) -> Set[int]:
        """Obtiene entidades en la celda que contiene el punto.
        
        Args:
            x: Posición X.
            y: Posición Y.
            
        Returns:
            Set de entity IDs en esa celda.
        """
        key = self._cell_key(x, y)
        return self._cells.get(key, set()).copy()

    def query_radius(self, x: float, y: float, radius: float) -> Set[int]:
        """Obtiene entidades que podrían colisionar con un círculo.
        
        Retorna todas las entidades en las celdas que cubre el círculo.
        Nota: Esto es broad-phase; se necesita narrow-phase para confirmar colisión.
        
        Args:
            x: Centro X del círculo.
            y: Centro Y del círculo.
            radius: Radio del círculo.
            
        Returns:
            Set de entity IDs candidatos.
        """
        x1, y1, x2, y2 = self._get_cell_range(x, y, radius)
        result: Set[int] = set()
        
        for cx in range(x1, x2 + 1):
            for cy in range(y1, y2 + 1):
                cell = self._cells.get((cx, cy))
                if cell:
                    result.update(cell)
        
        return result

    def query_rect(self, left: float, top: float, width: float, height: float) -> Set[int]:
        """Obtiene entidades que podrían colisionar con un rectángulo.
        
        Args:
            left: Coordenada X izquierda.
            top: Coordenada Y superior.
            width: Ancho del rectángulo.
            height: Alto del rectángulo.
            
        Returns:
            Set de entity IDs candidatos.
        """
        x1 = int(left * self._inv_cell_size)
        y1 = int(top * self._inv_cell_size)
        x2 = int((left + width) * self._inv_cell_size)
        y2 = int((top + height) * self._inv_cell_size)
        
        result: Set[int] = set()
        for cx in range(x1, x2 + 1):
            for cy in range(y1, y2 + 1):
                cell = self._cells.get((cx, cy))
                if cell:
                    result.update(cell)
        
        return result

    def clear(self) -> None:
        """Limpia todo el spatial hash."""
        self._cells.clear()
        self._entity_cells.clear()

    def rebuild(self, entities: Iterable[Tuple[int, float, float, float]]) -> None:
        """Reconstruye el spatial hash desde cero.
        
        Args:
            entities: Iterable de (eid, x, y, radius).
        """
        self.clear()
        for eid, x, y, radius in entities:
            self.insert(eid, x, y, radius)

    @property
    def cell_count(self) -> int:
        """Número de celdas activas."""
        return len(self._cells)

    @property
    def entity_count(self) -> int:
        """Número de entidades indexadas."""
        return len(self._entity_cells)

    def get_stats(self) -> dict:
        """Retorna estadísticas del spatial hash para debugging."""
        if not self._cells:
            return {
                "cells": 0,
                "entities": 0,
                "avg_per_cell": 0.0,
                "max_per_cell": 0,
            }
        
        sizes = [len(s) for s in self._cells.values()]
        return {
            "cells": len(self._cells),
            "entities": len(self._entity_cells),
            "avg_per_cell": sum(sizes) / len(sizes),
            "max_per_cell": max(sizes),
        }


# ── NPC feet spatial hash (rebuilt per frame by physics systems) ──

_npc_feet_hash: Optional[SpatialHash] = None
_npc_feet_hash_frame: int = -1  # frame counter when last rebuilt
_npc_feet_circles_cache: Dict[int, Tuple[float, float, float]] = {}
_npc_feet_rects_cache: Dict = {}


def build_npc_feet_hash(
    world,
    *,
    exclude_dead: bool = True,
    exclude_player: bool = True,
) -> Tuple[SpatialHash, Dict[int, Tuple[float, float, float]], Dict]:
    """Build a SpatialHash of NPC feet colliders for broad-phase NPC-NPC queries.

    Returns:
        (spatial_hash, feet_circles, feet_rects)
        - spatial_hash: SpatialHash populated with all qualifying NPCs.
        - feet_circles: {eid: (cx, cy, radius)} for circle colliders.
        - feet_rects: {eid: pygame.Rect} for rect colliders.

    The hash is rebuilt at most once per frame (cached by world._frame_count).
    """
    global _npc_feet_hash, _npc_feet_hash_frame
    global _npc_feet_circles_cache, _npc_feet_rects_cache

    import pygame
    from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world

    frame = getattr(world, '_frame_count', -1)

    # Return cached version if already built this frame
    if (
        _npc_feet_hash is not None
        and _npc_feet_hash_frame == frame
        and frame >= 0
    ):
        return _npc_feet_hash, _npc_feet_circles_cache, _npc_feet_rects_cache

    comps = world.components
    pos_map = comps.get('Position', {})
    multi_map = comps.get('MultiCollider', {})
    death_map = comps.get('DeathTimer', {})
    player_map = comps.get('PlayerTagComponent', {})

    sh = SpatialHash(cell_size=64)
    feet_circles: Dict[int, Tuple[float, float, float]] = {}
    feet_rects: Dict = {}

    for eid in world.get_entities_with('Position', 'MultiCollider'):
        if exclude_dead and eid in death_map:
            continue
        if exclude_player and eid in player_map:
            continue
        pos = pos_map[eid]
        multi = multi_map[eid]
        feet = multi.colliders.get('feet')
        if not feet:
            continue
        if hasattr(feet, 'radius'):
            cx, cy, r = get_circle_world(pos.x, pos.y, feet)
            feet_circles[eid] = (cx, cy, r)
            sh.insert(eid, cx, cy, r)
        else:
            rect = build_collider_rect(pos.x, pos.y, feet)
            feet_rects[eid] = rect
            rcx = rect.centerx
            rcy = rect.centery
            half_diag = ((rect.width ** 2 + rect.height ** 2) ** 0.5) * 0.5
            sh.insert(eid, rcx, rcy, half_diag)

    # Cache in module-level variables (SpatialHash uses __slots__)
    _npc_feet_hash = sh
    _npc_feet_hash_frame = frame
    _npc_feet_circles_cache = feet_circles
    _npc_feet_rects_cache = feet_rects

    return sh, feet_circles, feet_rects


# Cache global para entidades con Health (usada por FireballSystem)
_combat_spatial_hash: Optional[SpatialHash] = None


def get_combat_spatial_hash() -> SpatialHash:
    """Obtiene o crea el spatial hash global para combate."""
    global _combat_spatial_hash
    if _combat_spatial_hash is None:
        _combat_spatial_hash = SpatialHash(cell_size=64)
    return _combat_spatial_hash


def reset_combat_spatial_hash() -> None:
    """Resetea el spatial hash de combate."""
    global _combat_spatial_hash
    if _combat_spatial_hash is not None:
        _combat_spatial_hash.clear()
