"""
Módulo de pathfinding: A* simple.
"""
import heapq
from typing import List, Tuple

class PathFinder:
    """
    Implementa A* sobre la matriz de tiles.
    """
    def __heuristic(self, a: Tuple[int,int], b: Tuple[int,int]) -> float:
        return abs(a[0]-b[0]) + abs(a[1]-b[1])

    def find(self, start: Tuple[int,int], goal: Tuple[int,int], is_walkable=None) -> List[Tuple[int,int]]:
        """
        Encuentra camino desde start hasta goal. is_walkable(x,y) debe estar provisto.
        """
        if is_walkable is None:
            raise ValueError("Se requiere función is_walkable(x,y)")
        open_set = []
        heapq.heappush(open_set, (0, start))
        came_from = {}
        g_score = {start: 0}
        f_score = {start: self.__heuristic(start, goal)}

        while open_set:
            _, current = heapq.heappop(open_set)
            if current == goal:
                # reconstruir camino
                path = []
                while current in came_from:
                    path.append(current)
                    current = came_from[current]
                return path[::-1]

            x, y = current
            for dx, dy in [(1,0),(-1,0),(0,1),(0,-1)]:
                neighbor = (x+dx, y+dy)
                if not is_walkable(neighbor[0], neighbor[1]):
                    continue
                tentative_g = g_score[current] + 1
                if tentative_g < g_score.get(neighbor, float('inf')):
                    came_from[neighbor] = current
                    g_score[neighbor] = tentative_g
                    f_score[neighbor] = tentative_g + self.__heuristic(neighbor, goal)
                    heapq.heappush(open_set, (f_score[neighbor], neighbor))
        return []  # no path
