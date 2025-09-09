from typing import List, Tuple

class Zone:
    """
    Representa una región del mundo, con su propia matriz de caracteres,
    posición global y dimensiones.
    """
    def __init__(
        self,
        name: str,
        offset: Tuple[int, int],
        width: int,
        height: int,
    ):
        self.name = name
        self.offset_x, self.offset_y = offset
        self.width = width
        self.height = height
        # Matriz local de caracteres (#, ., O, etc.)
        self.matrix: List[List[str]] = [
            ["#" for _ in range(self.width)]
            for _ in range(self.height)
        ]
        # El modelo no mantiene referencias a recursos de render ni overlays.
        # Dichos datos deben ser gestionados por servicios/controladores externos.

    def set_matrix_from_rows(self, rows: List[str]) -> None:
        """
        Asigna la matriz local a partir de una lista de strings.
        """
        if len(rows) != self.height or any(len(r) != self.width for r in rows):
            raise ValueError(f"Dimensiones inválidas para zone '{self.name}': "
                             f"esperado {self.width}x{self.height}, "
                             f"recibido {len(rows)}x{len(rows[0]) if rows else 0}")
        self.matrix = [list(row) for row in rows]

    def global_coords(self, x: int, y: int) -> Tuple[int, int]:
        """
        Convierte coordenadas locales (dentro de la zona) a globales.
        """
        gx = self.offset_x + x
        gy = self.offset_y + y
        return gx, gy

    def local_coords(self, gx: int, gy: int) -> Tuple[int, int]:
        """
        Convierte coordenadas globales a locales relativas a esta zona.
        """
        return gx - self.offset_x, gy - self.offset_y

    def is_inside_local(self, x: int, y: int) -> bool:
        """
        Verifica si (x, y) locales caen dentro de los límites de la zona.
        """
        return 0 <= x < self.width and 0 <= y < self.height

    def is_inside_global(self, gx: int, gy: int) -> bool:
        """
        Verifica si (gx, gy) globales caen dentro de los límites de la zona.
        """
        lx, ly = self.local_coords(gx, gy)
        return self.is_inside_local(lx, ly)

    def __repr__(self):
        return (
            f"<Zone '{self.name}' size={self.width}x{self.height} "
            f"offset=({self.offset_x},{self.offset_y})>"
        )