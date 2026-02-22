import pygame
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
import math

def build_collider_rect(pos_x: float,
                        pos_y: float,
                        collider: Collider | MaskCollider | CircleCollider) -> pygame.Rect:
    """
    Retorna el pygame.Rect ubicado en (pos_x,pos_y) con el offset y tamaño del collider.
    """
    # Soporta Collider y MaskCollider (usa mask.get_size()) y CircleCollider (devuelve su AABB)
    if hasattr(collider, "mask"):
        w, h = collider.mask.get_size()
        return pygame.Rect(
            pos_x + collider.offset_x,
            pos_y + collider.offset_y,
            w,
            h
        )
    if hasattr(collider, "radius"):
        cx, cy, r = get_circle_world(pos_x, pos_y, collider)
        return pygame.Rect(int(cx - r), int(cy - r), int(r * 2), int(r * 2))
    # Collider rectangular
    w = collider.width
    h = getattr(collider, "height", collider.width)
    return pygame.Rect(
        pos_x + collider.offset_x,
        pos_y + collider.offset_y,
        w,
        h
    )


def get_circle_world(pos_x: float, pos_y: float, circle: CircleCollider) -> tuple[float, float, float]:
    """
    Devuelve (cx, cy, r) en coordenadas de mundo para un CircleCollider cuyo centro está
    en (pos_x + offset_x, pos_y + offset_y).
    """
    cx = pos_x + circle.offset_x
    cy = pos_y + circle.offset_y
    return float(cx), float(cy), float(circle.radius)


def circle_overlaps_rect(cx: float, cy: float, r: float, rect: pygame.Rect) -> bool:
    """Test preciso círculo vs AABB (pygame.Rect)."""
    # Clamp del centro al rectángulo para encontrar el punto más cercano
    closest_x = min(max(cx, rect.left), rect.right)
    closest_y = min(max(cy, rect.top), rect.bottom)
    dx = cx - closest_x
    dy = cy - closest_y
    return (dx * dx + dy * dy) <= (r * r)


def circle_overlaps_circle(c1: tuple[float, float, float], c2: tuple[float, float, float]) -> bool:
    """Test de solape entre dos círculos dados como (cx, cy, r)."""
    (x1, y1, r1) = c1
    (x2, y2, r2) = c2
    dx = x2 - x1
    dy = y2 - y1
    rr = r1 + r2
    return (dx * dx + dy * dy) <= (rr * rr)


def circle_circle_mtv(c1: tuple[float, float, float], c2: tuple[float, float, float]) -> tuple[float, float]:
    """
    Vector de traslación mínimo (MTV) para separar c1 de c2. Devuelve (0,0) si no hay solape.
    El MTV apunta desde c2 hacia c1.
    """
    (x1, y1, r1) = c1
    (x2, y2, r2) = c2
    dx = x1 - x2
    dy = y1 - y2
    dist2 = dx * dx + dy * dy
    rr = r1 + r2
    if dist2 <= 0.000001:
        # Centros coincidentes: empuje arbitrario en Y
        return (0.0, rr)
    if dist2 >= rr * rr:
        return (0.0, 0.0)
    dist = dist2 ** 0.5
    overlap = rr - dist
    nx = dx / dist
    ny = dy / dist
    return (nx * overlap, ny * overlap)


def circle_rect_mtv(cx: float, cy: float, r: float, rect: pygame.Rect) -> tuple[float, float]:
    """
    Vector de traslación mínimo (MTV) para separar un círculo de un AABB (pygame.Rect).
    Devuelve (0,0) si no hay solape. El MTV apunta hacia fuera del rect desde el centro del círculo.
    """
    # Punto más cercano del rect al centro del círculo
    closest_x = min(max(cx, rect.left), rect.right)
    closest_y = min(max(cy, rect.top), rect.bottom)
    dx = cx - closest_x
    dy = cy - closest_y
    dist2 = dx * dx + dy * dy
    if dist2 > r * r:
        return (0.0, 0.0)
    if dist2 > 0.000001:
        dist = dist2 ** 0.5
        overlap = r - dist
        nx = dx / dist
        ny = dy / dist
        return (nx * overlap, ny * overlap)
    # Centro dentro del rect o tangente exacto; empujar hacia el exterior por el eje más cercano
    # Magnitud correcta: r + distancia desde el centro a la pared más cercana
    dist_left   = cx - rect.left
    dist_right  = rect.right - cx
    dist_top    = cy - rect.top
    dist_bottom = rect.bottom - cy
    min_pen = min(dist_left, dist_right, dist_top, dist_bottom)
    if min_pen == dist_left:
        # Empujar hacia la izquierda (negativo X)
        return (-(r + dist_left), 0.0)
    if min_pen == dist_right:
        # Empujar hacia la derecha (positivo X)
        return ((r + dist_right), 0.0)
    if min_pen == dist_top:
        # Empujar hacia arriba (negativo Y)
        return (0.0, -(r + dist_top))
    # Empujar hacia abajo (positivo Y)
    return (0.0, (r + dist_bottom))


def _clamp(v: float, a: float, b: float) -> float:
    return a if v < a else (b if v > b else v)


def circle_overlaps_obb(cx: float, cy: float, r: float,
                        wx: float, wy: float,
                        half_w: float, half_h: float,
                        cos_a: float, sin_a: float) -> bool:
    """Preciso: círculo vs rectángulo orientado (OBB).
    Transforma el centro del círculo a espacio local del OBB y realiza test AABB.
    """
    # Transform world->local: p_local = R(-a) * (p - w)
    dx = cx - wx
    dy = cy - wy
    lx =  dx * cos_a + dy * sin_a
    ly = -dx * sin_a + dy * cos_a
    # Punto más cercano del AABB local al centro
    qx = _clamp(lx, -half_w, half_w)
    qy = _clamp(ly, -half_h, half_h)
    vx = lx - qx
    vy = ly - qy
    return (vx * vx + vy * vy) <= (r * r)


def circle_obb_mtv(cx: float, cy: float, r: float,
                   wx: float, wy: float,
                   half_w: float, half_h: float,
                   cos_a: float, sin_a: float) -> tuple[float, float]:
    """MTV de círculo vs OBB en coordenadas de mundo.
    Calcula MTV en espacio local y lo rota a mundo. Devuelve (0,0) si no hay solape.
    """
    # world->local
    dx = cx - wx
    dy = cy - wy
    lx =  dx * cos_a + dy * sin_a
    ly = -dx * sin_a + dy * cos_a
    # Punto más cercano en AABB local
    qx = _clamp(lx, -half_w, half_w)
    qy = _clamp(ly, -half_h, half_h)
    vx = lx - qx
    vy = ly - qy
    dist2 = vx * vx + vy * vy
    if dist2 > r * r:
        return (0.0, 0.0)
    if dist2 > 1e-9:
        dist = math.sqrt(dist2)
        overlap = r - dist
        nx = vx / dist
        ny = vy / dist
        # MTV en local, apuntando hacia fuera del rect
        mx = nx * overlap
        my = ny * overlap
    else:
        # Centro dentro: empujar hacia fuera por eje de menor penetración
        dl = lx + half_w
        dr = half_w - lx
        dt = ly + half_h
        db = half_h - ly
        mmin = min(dl, dr, dt, db)
        if mmin == dl:
            mx, my = -(r + dl), 0.0
        elif mmin == dr:
            mx, my =  (r + dr), 0.0
        elif mmin == dt:
            mx, my = 0.0, -(r + dt)
        else:
            mx, my = 0.0,  (r + db)
    # local->world: R(a) * m
    wx_m = mx * cos_a - my * sin_a
    wy_m = mx * sin_a + my * cos_a
    return (wx_m, wy_m)